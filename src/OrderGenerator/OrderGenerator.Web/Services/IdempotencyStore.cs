using System.Collections.Concurrent;
using OrderGenerator.Application.DTOs;

namespace OrderGenerator.Web.Services;

public class IdempotencyStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(5);

    public OrderResponseDto? GetResponse(string key)
    {
        if (_store.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
            return entry.Response;

        _store.TryRemove(key, out _);
        return null;
    }

    public void Store(string key, OrderResponseDto response)
    {
        _store[key] = new CacheEntry(response, DateTime.UtcNow.Add(_ttl));
    }

    public void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _store)
        {
            if (kvp.Value.Expiry <= now)
                _store.TryRemove(kvp.Key, out _);
        }
    }

    private sealed class CacheEntry
    {
        public OrderResponseDto Response { get; }
        public DateTime Expiry { get; }

        public CacheEntry(OrderResponseDto response, DateTime expiry)
        {
            Response = response;
            Expiry = expiry;
        }
    }
}
