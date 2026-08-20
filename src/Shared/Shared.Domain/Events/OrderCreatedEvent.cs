namespace Shared.Domain.Events;

public class OrderCreatedEvent
{
    public string OrderId { get; set; } = Guid.NewGuid().ToString("N")[..20];
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
