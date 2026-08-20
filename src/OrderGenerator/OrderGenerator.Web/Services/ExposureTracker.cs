using System.Collections.Concurrent;

namespace OrderGenerator.Web.Services;

public class ExposureTracker
{
    private readonly ConcurrentDictionary<string, decimal> _exposures = new();
    private const decimal Limit = 100_000_000m;

    private static readonly string[] Symbols = { "PETR4", "VALE3", "VIIA4" };

    public decimal GetCurrentExposure(string symbol)
    {
        return _exposures.TryGetValue(symbol, out var exposure) ? exposure : 0m;
    }

    public void UpdateExposure(string symbol, decimal orderExposure)
    {
        _exposures.AddOrUpdate(symbol, orderExposure, (_, current) => current + orderExposure);
    }

    public Dictionary<string, decimal> GetAllExposures()
    {
        var result = new Dictionary<string, decimal>();
        foreach (var symbol in Symbols)
        {
            result[symbol] = GetCurrentExposure(symbol);
        }
        return result;
    }

    public decimal GetLimit() => Limit;
}
