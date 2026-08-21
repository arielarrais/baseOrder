using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Infrastructure.Persistence;

public class OutboxDispatcherService : BackgroundService
{
    private readonly SqliteEventStore _store;
    private readonly Messaging.IEventBroker _eventBroker;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        SqliteEventStore store,
        Messaging.IEventBroker eventBroker,
        ILogger<OutboxDispatcherService> logger)
    {
        _store = store;
        _eventBroker = eventBroker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcherService starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _store.GetUnpublishedOutboxAsync(batchSize: 50);

                foreach (var message in messages)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await PublishAsync(message);
                    await _store.MarkOutboxPublishedAsync(message.Id);
                    _logger.LogDebug("Published outbox message {MessageId} to {Topic}", message.Id, message.Topic);
                }

                if (messages.Count == 0)
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching outbox messages; retrying in 2s");
                try
                {
                    await Task.Delay(2000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("OutboxDispatcherService stopped");
    }

    private async Task PublishAsync(OutboxRow message)
    {
        var eventType = Type.GetType(message.EventType, throwOnError: false);
        if (eventType == null)
        {
            _logger.LogError("Unknown event type '{EventType}' for outbox message {MessageId}; message will be skipped",
                message.EventType, message.Id);
            return;
        }

        var payload = JsonSerializer.Deserialize(message.Payload, eventType);
        if (payload == null)
        {
            _logger.LogError("Failed to deserialize outbox message {MessageId}; message will be skipped", message.Id);
            return;
        }

        await PublishTyped((dynamic)payload, message.Topic);
    }

    private Task PublishTyped<T>(T evt, string topic) where T : class
        => _eventBroker.PublishAsync(topic, evt);
}
