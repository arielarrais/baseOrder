using System.Collections.Concurrent;
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
    private bool _disposed;

    public bool IsConnected => _initiator?.IsLoggedOn ?? false;

    public FixClient(string configPath)
    {
        var settings = new SessionSettings(configPath);
        var storeFactory = new FileStoreFactory(settings);
        var logFactory = new FileLogFactory(settings);
        var messageFactory = new QuickFix.FIX44.MessageFactory();

        _handler = new FixClientHandler();
        _initiator = new SocketInitiator(_handler, storeFactory, settings, logFactory, messageFactory);
        _sessionId = settings.GetSessions().First();
    }

    public void Connect()
    {
        _initiator.Start();
    }

    public void Disconnect()
    {
        _initiator.Stop();
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

    public void FromApp(Message message, SessionID sessionID)
    {
        try
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"[FixClient] FromApp MsgType={msgType} Session={sessionID}");

            if (msgType == MsgType.EXECUTION_REPORT)
            {
                if (message is QuickFix.FIX44.ExecutionReport er)
                {
                    OnExecutionReport(er);
                }
                else
                {
                    Console.WriteLine($"[FixClient] ExecutionReport received but not typed as FIX44. Actual type: {message.GetType().FullName}");
                    var rawClOrdId = message.GetString(Tags.ClOrdID);
                    var rawExecType = message.GetString(Tags.ExecType);
                    var isAccepted = rawExecType == "0";
                    var rejectReason = message.IsSetField(Tags.Text) ? message.GetString(Tags.Text) : null;
                    Console.WriteLine($"[FixClient] Raw fields: ClOrdId={rawClOrdId} ExecType={rawExecType} Accepted={isAccepted}");
                    HandleRawExecutionReport(rawClOrdId, isAccepted, rejectReason);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FixClient] FromApp error: {ex}");
        }
    }

    private void OnExecutionReport(QuickFix.FIX44.ExecutionReport message)
    {
        var clOrdId = message.ClOrdID.Value;
        var execType = message.ExecType.Value;
        var isAccepted = execType == ExecType.NEW;
        var rejectReason = message.IsSetField(Tags.Text) ? message.Text.Value : null;

        Console.WriteLine($"[FixClient] ExecutionReport ClOrdId={clOrdId} ExecType={execType} Accepted={isAccepted}");
        ResolvePendingOrder(clOrdId, isAccepted, rejectReason);
    }

    private void HandleRawExecutionReport(string clOrdId, bool isAccepted, string? rejectReason)
    {
        Console.WriteLine($"[FixClient] Raw ExecutionReport ClOrdId={clOrdId} Accepted={isAccepted}");
        ResolvePendingOrder(clOrdId, isAccepted, rejectReason);
    }

    private void ResolvePendingOrder(string clOrdId, bool isAccepted, string? rejectReason)
    {
        if (_pendingOrders.TryRemove(clOrdId, out var tcs))
        {
            tcs.TrySetResult(new FixResponse(isAccepted, rejectReason));
            Console.WriteLine($"[FixClient] TCS completed for ClOrdId={clOrdId}");
        }
        else
        {
            Console.WriteLine($"[FixClient] No pending order for ClOrdId={clOrdId}. Pending count={_pendingOrders.Count}");
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