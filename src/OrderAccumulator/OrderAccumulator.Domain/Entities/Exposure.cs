using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Domain.Entities;

public class Exposure
{
    private readonly Dictionary<Symbol, Money> _exposures = new();
    private static readonly Money Limit = new(100_000_000m);

    public Exposure()
    {
        foreach (Symbol symbol in Enum.GetValues<Symbol>())
        {
            _exposures[symbol] = Money.Zero();
        }
    }

    public Money GetCurrentExposure(Symbol symbol)
    {
        return _exposures.TryGetValue(symbol, out var exposure)
            ? exposure
            : Money.Zero();
    }

    public bool CanAcceptOrder(Symbol symbol, Money orderValue)
    {
        var currentExposure = GetCurrentExposure(symbol);
        var newExposure = currentExposure + orderValue;
        return !newExposure.Abs().IsGreaterThan(Limit);
    }

    public void UpdateExposure(Symbol symbol, Money orderValue)
    {
        var currentExposure = GetCurrentExposure(symbol);
        _exposures[symbol] = currentExposure + orderValue;
    }

    public Dictionary<Symbol, Money> GetAllExposures()
    {
        return new Dictionary<Symbol, Money>(_exposures);
    }

    public Money GetLimit() => Limit;
}
