using System.Collections.Concurrent;

namespace OrderGenerator.Web.Services;

public class ExposureTracker
{
    private readonly ConcurrentDictionary<string, decimal> _exposures = new();
    private readonly ConcurrentDictionary<string, int> _quantities = new();
    private const decimal Limit = 100_000_000m;

    private static readonly string[] Symbols = { "PETR4", "VALE3", "VIIA4" };

    public decimal GetCurrentExposure(string symbol)
    {
        return _exposures.TryGetValue(symbol, out var exposure) ? exposure : 0m;
    }

    public int GetQuantity(string symbol)
    {
        return _quantities.TryGetValue(symbol, out var qty) ? qty : 0;
    }

    public void UpdateExposure(string symbol, decimal orderExposure, int quantity)
    {
        _exposures.AddOrUpdate(symbol, orderExposure, (_, current) => current + orderExposure);
        _quantities.AddOrUpdate(symbol, quantity, (_, current) => current + quantity);
    }

    public Dictionary<string, ExposureInfo> GetAllExposures()
    {
        var result = new Dictionary<string, ExposureInfo>();
        foreach (var symbol in Symbols)
        {
            result[symbol] = new ExposureInfo
            {
                Exposure = GetCurrentExposure(symbol),
                Quantity = GetQuantity(symbol)
            };
        }
        return result;
    }

    public decimal GetLimit() => Limit;
}

public class ExposureInfo
{
    public decimal Exposure { get; set; }
    public int Quantity { get; set; }
}
