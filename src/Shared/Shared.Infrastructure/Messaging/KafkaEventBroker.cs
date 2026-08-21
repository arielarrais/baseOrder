using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure.Messaging;

public class KafkaEventBroker : IEventBroker, IDisposable
{
    private readonly string _bootstrapServers;
    private readonly ILogger<KafkaEventBroker>? _logger;
    private readonly Lazy<IProducer<string, string>> _producer;
    private readonly ConcurrentDictionary<string, IConsumer<string, string>> _pollingConsumers = new();
    private readonly List<IConsumer<string, string>> _streamConsumers = new();
    private readonly HashSet<string> _ensuredTopics = new();
    private readonly object _syncRoot = new();
    private bool _disposed;

    public KafkaEventBroker(string bootstrapServers = "localhost:9092", ILogger<KafkaEventBroker>? logger = null)
    {
        _bootstrapServers = bootstrapServers;
        _logger = logger;
        _producer = new Lazy<IProducer<string, string>>(() => new ProducerBuilder<string, string>(
            new ProducerConfig
            {
                BootstrapServers = _bootstrapServers,
                Acks = Acks.All
            }).Build());
    }

    public async Task PublishAsync<T>(string topic, T evt) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureTopic(topic);

        var message = new Message<string, string>
        {
            Value = JsonSerializer.Serialize(evt),
            Headers = new Headers
            {
                { "MessageId", Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")) },
                { "EventType", Encoding.UTF8.GetBytes(typeof(T).AssemblyQualifiedName ?? typeof(T).Name) }
            }
        };

        await _producer.Value.ProduceAsync(topic, message);
    }

    public Task<T?> ConsumeAsync<T>(string topic, TimeSpan timeout) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureTopic(topic);

        var consumer = _pollingConsumers.GetOrAdd($"{topic}.consumer", groupId => CreateConsumer(groupId));

        ConsumeResult<string, string>? result;
        try
        {
            result = consumer.Consume(timeout);
        }
        catch (ConsumeException ex)
        {
            _logger?.LogError(ex, "Kafka consume error on topic {Topic}", topic);
            return Task.FromResult<T?>(null);
        }

        if (result == null)
            return Task.FromResult<T?>(null);

        T? evt;
        try
        {
            evt = JsonSerializer.Deserialize<T>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Invalid message payload on topic {Topic} at offset {Offset}",
                topic, result.Offset.Value);
            evt = null;
        }

        try
        {
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            _logger?.LogError(ex, "Failed to commit offset on topic {Topic}", topic);
        }

        return Task.FromResult(evt);
    }

    public void StartConsuming<T>(string topic, Func<T, Task> handler, CancellationToken ct) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureTopic(topic);

        var consumer = CreateConsumer($"{topic}.worker");
        lock (_syncRoot)
        {
            _streamConsumers.Add(consumer);
        }

        Task.Run(async () =>
        {
            try
            {
                consumer.Subscribe(topic);

                while (!ct.IsCancellationRequested)
                {
                    ConsumeResult<string, string> result;
                    try
                    {
                        result = consumer.Consume(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ConsumeException ex)
                    {
                        _logger?.LogError(ex, "Kafka consume error on topic {Topic}", topic);
                        continue;
                    }

                    try
                    {
                        var evt = JsonSerializer.Deserialize<T>(result.Message.Value);
                        if (evt != null)
                        {
                            await handler(evt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Handler error on topic {Topic}, offset {Offset}",
                            topic, result.Offset.Value);
                    }

                    try
                    {
                        consumer.Commit(result);
                    }
                    catch (KafkaException ex)
                    {
                        _logger?.LogError(ex, "Failed to commit offset on topic {Topic}", topic);
                    }
                }
            }
            finally
            {
                try
                {
                    consumer.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }, ct);
    }

    public void Subscribe<T>(string topic, Func<T, Task> handler) where T : class
    {
        // Kept for backward compatibility
    }

    private IConsumer<string, string> CreateConsumer(string groupId)
    {
        return new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
    }

    private void EnsureTopic(string topic)
    {
        lock (_syncRoot)
        {
            if (_ensuredTopics.Contains(topic))
                return;
        }

        try
        {
            using var admin = new AdminClientBuilder(
                new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();

            var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
            var exists = metadata.Topics.Any(t =>
                t.Topic == topic && t.Error.Code == ErrorCode.NoError);

            if (!exists)
            {
                admin.CreateTopicsAsync(new[]
                {
                    new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }
                }).GetAwaiter().GetResult();

                _logger?.LogInformation("Created Kafka topic {Topic}", topic);
            }

            lock (_syncRoot)
            {
                _ensuredTopics.Add(topic);
            }
        }
        catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            lock (_syncRoot)
            {
                _ensuredTopics.Add(topic);
            }
        }
        catch (KafkaException ex)
        {
            _logger?.LogWarning(ex,
                "Could not ensure topic {Topic} exists; produce/consume will retry", topic);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_syncRoot)
        {
            foreach (var consumer in _streamConsumers)
            {
                try
                {
                    consumer.Close();
                    consumer.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _streamConsumers.Clear();
        }

        foreach (var kvp in _pollingConsumers)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _pollingConsumers.Clear();

        if (_producer.IsValueCreated)
        {
            try
            {
                _producer.Value.Flush(TimeSpan.FromSeconds(5));
                _producer.Value.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}
