using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderGenerator.Application.Services;
using Shared.Domain.Events;
using Shared.Infrastructure.Messaging;

namespace OrderGenerator.Web.Services;

public class EventResultConsumerService : BackgroundService
{
    private readonly IEventBroker _eventBroker;
    private readonly IOrderService _orderService;
    private readonly ExposureTracker _exposureTracker;
    private readonly ILogger<EventResultConsumerService> _logger;

    public EventResultConsumerService(
        IEventBroker eventBroker,
        IOrderService orderService,
        ExposureTracker exposureTracker,
        ILogger<EventResultConsumerService> logger)
    {
        _eventBroker = eventBroker;
        _orderService = orderService;
        _exposureTracker = exposureTracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventResultConsumerService starting...");

        _eventBroker.StartConsuming<OrderProcessedEvent>("orders.processed", async evt =>
        {
            _logger.LogInformation("Received result for order {OrderId}: {Status}",
                evt.OrderId, evt.IsAccepted ? "Accepted" : "Rejected");

            _orderService.UpdateOrderStatus(evt.OrderId, evt);

            if (evt.IsAccepted)
            {
                var orderStatus = await _orderService.GetOrderStatusAsync(evt.OrderId);
                if (orderStatus != null)
                {
                    var sign = orderStatus.Side == "Compra" ? 1m : -1m;
                    var orderExposure = orderStatus.Price * orderStatus.Quantity * sign;
                    _exposureTracker.UpdateExposure(orderStatus.Symbol!, orderExposure, orderStatus.Quantity);
                }
            }

            await Task.CompletedTask;
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}
