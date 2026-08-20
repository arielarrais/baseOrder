using OrderAccumulator.Domain.Entities;
using OrderAccumulator.Domain.Enums;

namespace OrderAccumulator.Application.Interfaces;

public interface IOrderHandler
{
    Task<OrderResult> HandleNewOrderAsync(
        string clOrdId,
        Symbol symbol,
        Side side,
        int quantity,
        decimal price);
}

public class OrderResult
{
    public bool IsAccepted { get; set; }
    public string ClOrdId { get; set; } = string.Empty;
    public string? RejectReason { get; set; }
    public DateTime Timestamp { get; set; }
}
