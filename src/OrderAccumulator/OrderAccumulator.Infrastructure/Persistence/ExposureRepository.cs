using OrderAccumulator.Domain.Enums;
using OrderAccumulator.Domain.Interfaces;
using Shared.Domain.ValueObjects;
using Shared.Infrastructure.Persistence;

namespace OrderAccumulator.Infrastructure.Persistence;

public class ExposureRepository : IExposureRepository
{
    private readonly Dictionary<Symbol, Money> _exposures = new();
    private readonly Money _limit = new(100_000_000m);
    private readonly SqliteEventStore? _store;
    private readonly object _lock = new();

    public ExposureRepository(SqliteEventStore? store = null)
    {
        _store = store;

        foreach (Symbol symbol in Enum.GetValues<Symbol>())
        {
            _exposures[symbol] = Money.Zero();
        }

        if (_store != null)
        {
            LoadPersistedExposures();
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
        Money updated;
        lock (_lock)
        {
            var currentExposure = GetCurrentExposure(symbol);
            updated = currentExposure + orderValue;
            _exposures[symbol] = updated;
        }

        _store?.UpsertExposureAsync(symbol.ToString(), updated.Amount).GetAwaiter().GetResult();
    }

    public Dictionary<Symbol, Money> GetAllExposures()
    {
        lock (_lock)
        {
            return new Dictionary<Symbol, Money>(_exposures);
        }
    }

    public Money GetLimit() => _limit;

    private void LoadPersistedExposures()
    {
        try
        {
            var persisted = _store!.GetExposuresAsync().GetAwaiter().GetResult();
            foreach (var (symbolName, value) in persisted)
            {
                if (Enum.TryParse<Symbol>(symbolName, ignoreCase: true, out var symbol))
                {
                    _exposures[symbol] = new Money(value);
                }
            }
        }
        catch
        {
            // Database unavailable at startup: start from zero rather than fail the worker
        }
    }
}
