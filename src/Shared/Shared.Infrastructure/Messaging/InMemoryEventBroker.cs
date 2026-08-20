using System.Collections.Concurrent;
using System.Text.Json;

namespace Shared.Infrastructure.Messaging;

public class InMemoryEventBroker : IEventBroker
{
    private readonly ConcurrentDictionary<string, BlockingCollection<string>> _topics = new();

    private BlockingCollection<string> GetTopic(string topic)
    {
        return _topics.GetOrAdd(topic, _ => new BlockingCollection<string>(boundedCapacity: 1000));
    }

    public Task PublishAsync<T>(string topic, T evt) where T : class
    {
        var json = JsonSerializer.Serialize(evt);
        var queue = GetTopic(topic);
        queue.Add(json);
        return Task.CompletedTask;
    }

    public Task<T?> ConsumeAsync<T>(string topic, TimeSpan timeout) where T : class
    {
        var queue = GetTopic(topic);
        var cts = new CancellationTokenSource(timeout);

        if (queue.TryTake(out var json, timeout))
        {
            var result = JsonSerializer.Deserialize<T>(json);
            return Task.FromResult(result);
        }

        return Task.FromResult<T?>(null);
    }

    public void StartConsuming<T>(string topic, Func<T, Task> handler, CancellationToken ct) where T : class
    {
        var queue = GetTopic(topic);

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (queue.TryTake(out var json, 1000))
                    {
                        var evt = JsonSerializer.Deserialize<T>(json);
                        if (evt != null)
                        {
                            await handler(evt);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Log and continue
                }
            }
        }, ct);
    }

    public void Subscribe<T>(string topic, Func<T, Task> handler) where T : class
    {
        // Kept for backward compatibility
    }
}
