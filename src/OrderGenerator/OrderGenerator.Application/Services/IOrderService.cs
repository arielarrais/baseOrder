using OrderGenerator.Application.DTOs;

namespace OrderGenerator.Application.Services;

public interface IOrderService
{
    Task<OrderResponseDto> SendOrderAsync(OrderDto order);
}
