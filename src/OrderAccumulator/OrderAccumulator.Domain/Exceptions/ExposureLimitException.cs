using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Domain.Exceptions;

public class ExposureLimitException : Exception
{
    public Symbol Symbol { get; }
    public Money CurrentExposure { get; }
    public Money OrderValue { get; }
    public Money Limit { get; }

    public ExposureLimitException(
        Symbol symbol,
        Money currentExposure,
        Money orderValue,
        Money limit)
        : base($"Exposure limit exceeded for {symbol}. " +
               $"Current: {currentExposure}, Order: {orderValue}, Limit: {limit}")
    {
        Symbol = symbol;
        CurrentExposure = currentExposure;
        OrderValue = orderValue;
        Limit = limit;
    }
}
