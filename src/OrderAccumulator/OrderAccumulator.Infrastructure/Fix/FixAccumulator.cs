using Microsoft.Extensions.Logging;
using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Domain.Enums;
using QuickFix;
using QuickFix.Fields;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;

namespace OrderAccumulator.Infrastructure.Fix;

public class FixAccumulator : MessageCracker, IApplication, IDisposable
{
    private readonly IOrderHandler _orderHandler;
    private readonly ILogger<FixAccumulator> _logger;
    private readonly ThreadedSocketAcceptor _acceptor;
    private bool _disposed;

    public FixAccumulator(
        IOrderHandler orderHandler,
        ILogger<FixAccumulator> logger,
        string configPath)
    {
        _orderHandler = orderHandler ?? throw new ArgumentNullException(nameof(orderHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var settings = new SessionSettings(configPath);
        var storeFactory = new FileStoreFactory(settings);
        var logFactory = new FileLogFactory(settings);
        var messageFactory = new QuickFix.FIX44.MessageFactory();

        _acceptor = new ThreadedSocketAcceptor(this, storeFactory, settings, logFactory, messageFactory);
    }

    public void Start()
    {
        _acceptor.Start();
        _logger.LogInformation("FIX Acceptor started");
    }

    public void Stop()
    {
        _acceptor.Stop();
        _logger.LogInformation("FIX Acceptor stopped");
    }

    public void OnMessage(QuickFix.FIX44.NewOrderSingle message, SessionID sessionID)
    {
        try
        {
            var clOrdId = message.ClOrdID.Value;
            var symbol = Enum.Parse<Domain.Enums.Symbol>(message.Symbol.Value);
            var side = message.Side.Value == QuickFix.Fields.Side.BUY
                ? Domain.Enums.Side.Buy
                : Domain.Enums.Side.Sell;
            var quantity = (int)message.OrderQty.Value;
            var price = message.Price.Value;

            _logger.LogInformation(
                "Received order: {ClOrdId} {Symbol} {Side} {Quantity} @ {Price}",
                clOrdId, symbol, side, quantity, price);

            var result = _orderHandler.HandleNewOrderAsync(
                clOrdId, symbol, side, quantity, price).GetAwaiter().GetResult();

            SendExecutionReport(sessionID, message, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order");
            SendReject(sessionID, message, ex.Message);
        }
    }

    private void SendExecutionReport(
        SessionID sessionID,
        QuickFix.FIX44.NewOrderSingle originalMessage,
        OrderResult result)
    {
        var report = new QuickFix.FIX44.ExecutionReport();
        report.SetField(new OrderID(Guid.NewGuid().ToString("N")[..20]));
        report.SetField(new ExecID(Guid.NewGuid().ToString("N")[..20]));
        report.SetField(new ExecType(result.IsAccepted ? ExecType.NEW : ExecType.REJECTED));
        report.SetField(new OrdStatus(result.IsAccepted ? OrdStatus.FILLED : OrdStatus.REJECTED));
        report.SetField(new ClOrdID(result.ClOrdId));
        report.SetField(new QuickFix.Fields.Symbol(originalMessage.Symbol.Value));
        report.SetField(new QuickFix.Fields.Side(originalMessage.Side.Value));
        report.SetField(new OrderQty(originalMessage.OrderQty.Value));
        report.SetField(new Price(originalMessage.Price.Value));
        report.SetField(new TransactTime());
        report.SetField(new AvgPx(0));
        report.SetField(new CumQty(0));
        report.SetField(new LeavesQty(originalMessage.OrderQty.Value));

        if (!result.IsAccepted && result.RejectReason != null)
        {
            report.SetField(new Text(result.RejectReason));
        }

        var session = Session.LookupSession(sessionID);
        if (session != null)
        {
            session.Send(report);
        }
        else
        {
            _logger.LogWarning("Session not found for {SessionID}, cannot send ExecutionReport", sessionID);
        }

        _logger.LogInformation(
            "Sent ExecutionReport: {ClOrdId} - {Status}",
            result.ClOrdId,
            result.IsAccepted ? "Accepted" : "Rejected");
    }

    private void SendReject(
        SessionID sessionID,
        QuickFix.FIX44.NewOrderSingle originalMessage,
        string reason)
    {
        var reject = new QuickFix.FIX44.Reject();
        reject.SetField(new RefMsgType(originalMessage.Header.GetString(Tags.MsgType)));
        reject.SetField(new Text(reason));

        var session = Session.LookupSession(sessionID);
        session?.Send(reject);
    }

    public void FromApp(Message message, SessionID sessionID)
    {
        Crack(message, sessionID);
    }

    public void FromAdmin(Message message, SessionID sessionID) { }
    public void ToApp(Message message, SessionID sessionID) { }
    public void ToAdmin(Message message, SessionID sessionID) { }

    public void OnCreate(SessionID sessionID) { }
    public void OnLogout(SessionID sessionID) { }
    public void OnLogon(SessionID sessionID) { }

    public void Dispose()
    {
        if (!_disposed)
        {
            _acceptor?.Stop();
            _acceptor?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
