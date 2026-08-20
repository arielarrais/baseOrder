using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Domain.Entities;

public class Order
{
    public Guid Id { get; }
    public string ClOrdId { get; }
    public Symbol Symbol { get; }
    public Side Side { get; }
    public int Quantity { get; }
    public Money Price { get; }
    public DateTime TransactTime { get; }
    public OrderStatus Status { get; private set; }
    public string? RejectReason { get; private set; }

    public Order(
        string clOrdId,
        Symbol symbol,
        Side side,
        int quantity,
        Money price)
    {
        Id = Guid.NewGuid();
        ClOrdId = clOrdId ?? throw new ArgumentNullException(nameof(clOrdId));
        Symbol = symbol;
        Side = side;
        Quantity = quantity > 0 ? quantity : throw new ArgumentException("Quantity must be positive");
        Price = price ?? throw new ArgumentNullException(nameof(price));
        TransactTime = DateTime.UtcNow;
        Status = OrderStatus.New;
    }

    public Money CalculateExposure()
    {
        var amount = Price.Amount * Quantity;
        var sign = Side == Side.Buy ? 1 : -1;
        return new Money(amount * sign);
    }

    public void Accept()
    {
        Status = OrderStatus.Filled;
    }

    public void Reject(string reason)
    {
        Status = OrderStatus.Rejected;
        RejectReason = reason;
    }
}

public enum OrderStatus
{
    New,
    Filled,
    Rejected
}
