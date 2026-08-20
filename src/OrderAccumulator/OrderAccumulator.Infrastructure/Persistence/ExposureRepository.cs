using OrderAccumulator.Domain.Enums;
using OrderAccumulator.Domain.Interfaces;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Infrastructure.Persistence;

public class ExposureRepository : IExposureRepository
{
    private readonly Dictionary<Symbol, Money> _exposures = new();
    private readonly Money _limit = new(100_000_000m);
    private readonly object _lock = new();

    public ExposureRepository()
    {
        foreach (Symbol symbol in Enum.GetValues<Symbol>())
        {
            _exposures[symbol] = Money.Zero();
        }
    }

    public Money GetCurrentExposure(Symbol symbol)
    {
        lock (_lock)
        {
            return _exposures.TryGetValue(symbol, out var exposure)
                ? exposure
                : Money.Zero();
        }
    }

    public bool CanAcceptOrder(Symbol symbol, Money orderValue)
    {
        lock (_lock)
        {
            var currentExposure = GetCurrentExposure(symbol);
            var newExposure = currentExposure + orderValue;
            return !newExposure.Abs().IsGreaterThan(_limit);
        }
    }

    public void UpdateExposure(Symbol symbol, Money orderValue)
    {
        lock (_lock)
        {
            var currentExposure = GetCurrentExposure(symbol);
            _exposures[symbol] = currentExposure + orderValue;
        }
    }

    public Dictionary<Symbol, Money> GetAllExposures()
    {
        lock (_lock)
        {
            return new Dictionary<Symbol, Money>(_exposures);
        }
    }

    public Money GetLimit() => _limit;
}
