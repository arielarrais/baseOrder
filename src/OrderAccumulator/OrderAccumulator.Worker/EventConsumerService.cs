using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Domain.Enums;
using Shared.Domain.Events;
using Shared.Infrastructure.Messaging;

namespace OrderAccumulator.Worker;

public class EventConsumerService : BackgroundService
{
    private readonly IEventBroker _eventBroker;
    private readonly IOrderHandler _orderHandler;
    private readonly ILogger<EventConsumerService> _logger;

    public EventConsumerService(
        IEventBroker eventBroker,
        IOrderHandler orderHandler,
        ILogger<EventConsumerService> logger)
    {
        _eventBroker = eventBroker;
        _orderHandler = orderHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventConsumerService starting...");

        _eventBroker.StartConsuming<OrderCreatedEvent>("orders.created", async evt =>
        {
            _logger.LogInformation("Processing order {OrderId}: {Symbol} {Side} {Qty} @ {Price}",
                evt.OrderId, evt.Symbol, evt.Side, evt.Quantity, evt.Price);

            try
            {
                var symbol = Enum.Parse<Symbol>(evt.Symbol);
                var side = evt.Side == "Compra" ? Side.Buy : Side.Sell;

                var result = await _orderHandler.HandleNewOrderAsync(
                    evt.OrderId, symbol, side, evt.Quantity, evt.Price);

                var processedEvent = new OrderProcessedEvent
                {
                    OrderId = evt.OrderId,
                    IsAccepted = result.IsAccepted,
                    RejectReason = result.RejectReason,
                    ProcessedAt = DateTime.Now
                };

                await _eventBroker.PublishAsync("orders.processed", processedEvent);

                _logger.LogInformation("Order {OrderId} processed: {Status}",
                    evt.OrderId, result.IsAccepted ? "Accepted" : "Rejected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order {OrderId}", evt.OrderId);

                var errorEvent = new OrderProcessedEvent
                {
                    OrderId = evt.OrderId,
                    IsAccepted = false,
                    RejectReason = ex.Message,
                    ProcessedAt = DateTime.Now
                };

                await _eventBroker.PublishAsync("orders.processed", errorEvent);
            }
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}
