namespace Shared.Domain.ValueObjects;

public record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "BRL")
    {
        Amount = Math.Round(amount, 2);
        Currency = currency;
    }

    public static Money Zero(string currency = "BRL") => new(0, currency);

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException("Cannot add different currencies");

        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException("Cannot subtract different currencies");

        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money left, int right)
    {
        return new Money(left.Amount * right, left.Currency);
    }

    public static Money operator *(Money left, decimal right)
    {
        return new Money(left.Amount * right, left.Currency);
    }

    public bool IsGreaterThan(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot compare different currencies");

        return Amount > other.Amount;
    }

    public bool IsLessThan(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot compare different currencies");

        return Amount < other.Amount;
    }

    public Money Abs() => new(Math.Abs(Amount), Currency);

    public override string ToString() => $"{Amount:F2} {Currency}";
}
