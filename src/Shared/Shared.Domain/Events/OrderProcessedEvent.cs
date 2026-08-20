namespace Shared.Domain.Events;

public class OrderProcessedEvent
{
    public string OrderId { get; set; } = string.Empty;
    public bool IsAccepted { get; set; }
    public string? RejectReason { get; set; }
    public decimal CurrentExposure { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.Now;
}
