using System.Collections.Concurrent;
using System.Text.Json;
using OrderGenerator.Application.DTOs;
using Polly;
using Polly.Retry;
using Shared.Domain.Events;
using Shared.Infrastructure.Fix;
using Shared.Infrastructure.Messaging;

namespace OrderGenerator.Application.Services;

public class OrderService : IOrderService
{
    private readonly IFixClient _fixClient;
    private readonly IEventBroker _eventBroker;
    private readonly ResiliencePipeline _retryPipeline;

    private static readonly ConcurrentDictionary<string, OrderStatus> _orderStatuses = new();

    public OrderService(IFixClient fixClient, IEventBroker eventBroker)
    {
        _fixClient = fixClient ?? throw new ArgumentNullException(nameof(fixClient));
        _eventBroker = eventBroker ?? throw new ArgumentNullException(nameof(eventBroker));

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

        // Set status to Pending immediately
        _orderStatuses[orderId] = new OrderStatus
        {
            OrderId = orderId,
            Symbol = order.Symbol,
            Side = order.Side,
            Quantity = order.Quantity,
            Price = order.Price,
            Status = "Pending",
            Timestamp = DateTime.Now
        };

        // Publish event (fire and forget - async)
        var evt = new OrderCreatedEvent
        {
            OrderId = orderId,
            Symbol = order.Symbol,
            Side = order.Side,
            Quantity = order.Quantity,
            Price = order.Price
        };

        await _eventBroker.PublishAsync("orders.created", evt);

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
        if (_orderStatuses.TryGetValue(orderId, out var status))
            return status;

        return null;
    }

    public void UpdateOrderStatus(string orderId, OrderProcessedEvent evt)
    {
        if (_orderStatuses.TryGetValue(orderId, out var status))
        {
            status.Status = evt.IsAccepted ? "Accepted" : "Rejected";
            status.RejectReason = evt.RejectReason;
            status.CurrentExposure = evt.CurrentExposure;
            status.ProcessedAt = evt.ProcessedAt;
        }
    }

    public static void InitializeFromEvent(OrderProcessedEvent evt)
    {
        _orderStatuses[evt.OrderId] = new OrderStatus
        {
            OrderId = evt.OrderId,
            Status = evt.IsAccepted ? "Accepted" : "Rejected",
            RejectReason = evt.RejectReason,
            CurrentExposure = evt.CurrentExposure,
            ProcessedAt = evt.ProcessedAt
        };
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
