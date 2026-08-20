namespace Shared.Infrastructure.Messaging;

public interface IEventBroker
{
    Task PublishAsync<T>(string topic, T evt) where T : class;
    Task<T?> ConsumeAsync<T>(string topic, TimeSpan timeout) where T : class;
    void Subscribe<T>(string topic, Func<T, Task> handler) where T : class;
    void StartConsuming<T>(string topic, Func<T, Task> handler, CancellationToken ct) where T : class;
}
