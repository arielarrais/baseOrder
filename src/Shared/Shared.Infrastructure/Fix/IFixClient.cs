using QuickFix.Fields;

namespace Shared.Infrastructure.Fix;

public interface IFixClient : IDisposable
{
    void Connect();
    void Disconnect();
    bool IsConnected { get; }
    Task<FixResponse> SendNewOrderSingleAndWaitAsync(
        string symbol,
        Side side,
        int quantity,
        decimal price,
        TimeSpan timeout);
}

public record FixResponse(bool IsAccepted, string? RejectReason);
