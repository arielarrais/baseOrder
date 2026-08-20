using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Shared.Infrastructure.Messaging;

public class RabbitMQEventBroker : IEventBroker, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private bool _disposed;

    public RabbitMQEventBroker(string hostName = "localhost", int port = 5672,
        string userName = "guest", string password = "guest")
    {
        var factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public Task PublishAsync<T>(string topic, T evt) where T : class
    {
        _channel.ExchangeDeclare(exchange: topic, type: ExchangeType.Fanout, durable: true);

        var workerQueue = $"queue.{topic}.worker";
        _channel.QueueDeclare(queue: workerQueue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue: workerQueue, exchange: topic, routingKey: string.Empty);

        var json = JsonSerializer.Serialize(evt);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = false;
        properties.MessageId = Guid.NewGuid().ToString("N");
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        properties.Type = typeof(T).AssemblyQualifiedName;

        _channel.BasicPublish(
            exchange: topic,
            routingKey: string.Empty,
            basicProperties: properties,
            body: body);

        return Task.CompletedTask;
    }

    public Task<T?> ConsumeAsync<T>(string topic, TimeSpan timeout) where T : class
    {
        _channel.ExchangeDeclare(exchange: topic, type: ExchangeType.Fanout, durable: true);
        var queueName = $"queue.{topic}.consumer";
        _channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue: queueName, exchange: topic, routingKey: string.Empty);

        var result = _channel.BasicGet(queue: queueName, autoAck: true);
        if (result == null)
            return Task.FromResult<T?>(null);

        var json = Encoding.UTF8.GetString(result.Body.ToArray());
        var evt = JsonSerializer.Deserialize<T>(json);
        return Task.FromResult(evt);
    }

    public void StartConsuming<T>(string topic, Func<T, Task> handler, CancellationToken ct) where T : class
    {
        _channel.ExchangeDeclare(exchange: topic, type: ExchangeType.Fanout, durable: true);
        var queueName = $"queue.{topic}.worker";
        _channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue: queueName, exchange: topic, routingKey: string.Empty);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var evt = JsonSerializer.Deserialize<T>(json);

                if (evt != null)
                {
                    await handler(evt);
                }
            }
            catch (Exception)
            {
                // Log and continue
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
    }

    public void Subscribe<T>(string topic, Func<T, Task> handler) where T : class
    {
        // Kept for backward compatibility
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
