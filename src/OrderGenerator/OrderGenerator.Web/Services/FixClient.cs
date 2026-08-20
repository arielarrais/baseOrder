using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuickFix;
using QuickFix.Fields;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;
using Shared.Infrastructure.Fix;

namespace OrderGenerator.Web.Services;

public class FixClient : IFixClient
{
    private readonly SocketInitiator _initiator;
    private readonly SessionID _sessionId;
    private readonly FixClientHandler _handler;
    private readonly ILogger<FixClient> _logger;
    private bool _disposed;

    public bool IsConnected => _initiator?.IsLoggedOn ?? false;

    public FixClient(string configPath, ILogger<FixClient> logger)
    {
        _logger = logger;
        var settings = new SessionSettings(configPath);
        var storeFactory = new FileStoreFactory(settings);
        var logFactory = new FileLogFactory(settings);
        var messageFactory = new QuickFix.FIX44.MessageFactory();

        _handler = new FixClientHandler(logger);
        _initiator = new SocketInitiator(_handler, storeFactory, settings, logFactory, messageFactory);
        _sessionId = settings.GetSessions().First();
    }

    public void Connect()
    {
        _initiator.Start();
        _logger.LogInformation("FIX Initiator connecting to {Session}", _sessionId);
    }

    public void Disconnect()
    {
        _initiator.Stop();
        _logger.LogInformation("FIX Initiator disconnected");
    }

    public async Task<FixResponse> SendNewOrderSingleAndWaitAsync(
        string symbol,
        Side side,
        int quantity,
        decimal price,
        TimeSpan timeout)
    {
        var clOrdId = Guid.NewGuid().ToString("N")[..20];

        var responseTask = _handler.RegisterPendingOrder(clOrdId, timeout);

        var order = new QuickFix.FIX44.NewOrderSingle();
        order.SetField(new ClOrdID(clOrdId));
        order.SetField(new Symbol(symbol));
        order.SetField(side);
        order.SetField(new TransactTime());
        order.SetField(new OrdType(OrdType.LIMIT));
        order.SetField(new OrderQty(quantity));
        order.SetField(new Price(price));
        order.SetField(new TimeInForce(TimeInForce.GOOD_TILL_CANCEL));

        var session = Session.LookupSession(_sessionId);
        if (session != null && session.IsLoggedOn)
        {
            session.Send(order);
            _logger.LogInformation("Order sent: ClOrdId={ClOrdId} {Symbol} {Side} {Qty} @ {Price}",
                clOrdId, symbol, side.getValue(), quantity, price);
        }
        else
        {
            _handler.CancelPendingOrder(clOrdId);
            throw new InvalidOperationException("FIX session not connected");
        }

        return await responseTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _initiator?.Stop();
            _initiator?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

internal class FixClientHandler : IApplication
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<FixResponse>> _pendingOrders = new();
    private readonly ILogger _logger;

    public FixClientHandler(ILogger logger)
    {
        _logger = logger;
    }

    public void FromApp(Message message, SessionID sessionID)
    {
        try
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            _logger.LogDebug("FIX FromApp MsgType={MsgType} Session={Session}", msgType, sessionID);

            if (msgType == MsgType.EXECUTION_REPORT)
            {
                if (message is QuickFix.FIX44.ExecutionReport er)
                {
                    OnExecutionReport(er);
                }
                else
                {
                    var rawClOrdId = message.GetString(Tags.ClOrdID);
                    var rawExecType = message.GetString(Tags.ExecType);
                    var isAccepted = rawExecType == "0";
                    var rejectReason = message.IsSetField(Tags.Text) ? message.GetString(Tags.Text) : null;
                    _logger.LogWarning("ExecutionReport not typed as FIX44. ClOrdId={ClOrdId} ExecType={ExecType}",
                        rawClOrdId, rawExecType);
                    HandleRawExecutionReport(rawClOrdId, isAccepted, rejectReason);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIX FromApp error");
        }
    }

    private void OnExecutionReport(QuickFix.FIX44.ExecutionReport message)
    {
        var clOrdId = message.ClOrdID.Value;
        var execType = message.ExecType.Value;
        var isAccepted = execType == ExecType.NEW;
        var rejectReason = message.IsSetField(Tags.Text) ? message.Text.Value : null;

        _logger.LogInformation("ExecutionReport: ClOrdId={ClOrdId} ExecType={ExecType} Accepted={Accepted}",
            clOrdId, execType, isAccepted);
        ResolvePendingOrder(clOrdId, isAccepted, rejectReason);
    }

    private void HandleRawExecutionReport(string clOrdId, bool isAccepted, string? rejectReason)
    {
        _logger.LogWarning("Raw ExecutionReport: ClOrdId={ClOrdId} Accepted={Accepted}", clOrdId, isAccepted);
        ResolvePendingOrder(clOrdId, isAccepted, rejectReason);
    }

    private void ResolvePendingOrder(string clOrdId, bool isAccepted, string? rejectReason)
    {
        if (_pendingOrders.TryRemove(clOrdId, out var tcs))
        {
            tcs.TrySetResult(new FixResponse(isAccepted, rejectReason));
            _logger.LogDebug("TCS completed for ClOrdId={ClOrdId}", clOrdId);
        }
        else
        {
            _logger.LogWarning("No pending order for ClOrdId={ClOrdId}. PendingCount={Count}",
                clOrdId, _pendingOrders.Count);
        }
    }

    public Task<FixResponse> RegisterPendingOrder(string clOrdId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<FixResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOrders[clOrdId] = tcs;

        var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() =>
        {
            if (_pendingOrders.TryRemove(clOrdId, out var pending))
                pending.TrySetResult(new FixResponse(false, "Response timeout"));
            cts.Dispose();
        });

        return tcs.Task;
    }

    public void CancelPendingOrder(string clOrdId)
    {
        if (_pendingOrders.TryRemove(clOrdId, out var tcs))
            tcs.TrySetResult(new FixResponse(false, "FIX session not connected"));
    }

    public void FromAdmin(Message message, SessionID sessionID) { }
    public void ToApp(Message message, SessionID sessionID) { }
    public void ToAdmin(Message message, SessionID sessionID) { }
    public void OnCreate(SessionID sessionID) { }
    public void OnLogout(SessionID sessionID) { }
    public void OnLogon(SessionID sessionID) { }
}