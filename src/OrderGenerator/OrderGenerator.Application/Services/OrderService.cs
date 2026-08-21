using OrderGenerator.Application.DTOs;
using Polly;
using Polly.Retry;
using Shared.Domain.Events;
using Shared.Infrastructure.Fix;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace OrderGenerator.Application.Services;

public class OrderService : IOrderService
{
    private readonly IFixClient _fixClient;
    private readonly IEventBroker _eventBroker;
    private readonly SqliteEventStore _store;
    private readonly ResiliencePipeline _retryPipeline;

    public OrderService(IFixClient fixClient, IEventBroker eventBroker, SqliteEventStore store)
    {
        _fixClient = fixClient ?? throw new ArgumentNullException(nameof(fixClient));
        _eventBroker = eventBroker ?? throw new ArgumentNullException(nameof(eventBroker));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<InvalidOperationException>()
                    .Handle<TimeoutException>(),
                OnRetry = args =>
                {
                    Console.WriteLine($"[OrderService] Retry {args.AttemptNumber} after {args.RetryDelay.TotalSeconds}s");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<OrderResponseDto> SendOrderAsync(OrderDto order)
    {
        var orderId = Guid.NewGuid().ToString("N")[..20];

        var evt = new OrderCreatedEvent
        {
            OrderId = orderId,
            Symbol = order.Symbol,
            Side = order.Side,
            Quantity = order.Quantity,
            Price = order.Price
        };

        await _retryPipeline.ExecuteAsync(async token =>
            await _store.CreatePendingOrderWithOutboxAsync(
                orderId, order.Symbol, order.Side, order.Quantity, order.Price,
                DateTime.Now, "orders.created", evt));

        return new OrderResponseDto
        {
            IsAccepted = false,
            ClOrdId = orderId,
            RejectReason = null,
            Status = "Pending",
            Timestamp = DateTime.Now
        };
    }

    public async Task<OrderStatus?> GetOrderStatusAsync(string orderId)
    {
        var row = await _store.GetOrderAsync(orderId);
        if (row == null)
            return null;

        return new OrderStatus
        {
            OrderId = row.OrderId,
            Symbol = row.Symbol,
            Side = row.Side,
            Quantity = (int)row.Quantity,
            Price = row.Price,
            Status = row.Status,
            RejectReason = row.RejectReason,
            CurrentExposure = row.CurrentExposure ?? 0m,
            Timestamp = row.CreatedAt,
            ProcessedAt = row.ProcessedAt
        };
    }

    public void UpdateOrderStatus(string orderId, OrderProcessedEvent evt)
    {
        _store.UpdateOrderResultAsync(evt).GetAwaiter().GetResult();
    }
}

public class OrderStatus
{
    public string OrderId { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? Side { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "Pending";
    public string? RejectReason { get; set; }
    public decimal CurrentExposure { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
