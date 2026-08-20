using OrderGenerator.Application.DTOs;

namespace OrderGenerator.Application.Services;

public interface IOrderService
{
    Task<OrderResponseDto> SendOrderAsync(OrderDto order);
    Task<OrderStatus?> GetOrderStatusAsync(string orderId);
    void UpdateOrderStatus(string orderId, Shared.Domain.Events.OrderProcessedEvent evt);
}
