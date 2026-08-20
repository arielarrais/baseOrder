using OrderGenerator.Application.DTOs;
using Shared.Infrastructure.Fix;

namespace OrderGenerator.Application.Services;

public class OrderService : IOrderService
{
    private readonly IFixClient _fixClient;

    public OrderService(IFixClient fixClient)
    {
        _fixClient = fixClient ?? throw new ArgumentNullException(nameof(fixClient));
    }

    public async Task<OrderResponseDto> SendOrderAsync(OrderDto order)
    {
        try
        {
            if (!_fixClient.IsConnected)
            {
                return new OrderResponseDto
                {
                    IsAccepted = false,
                    RejectReason = "FIX client not connected",
                    Timestamp = DateTime.UtcNow
                };
            }

            var side = order.Side == "Compra"
                ? new QuickFix.Fields.Side(QuickFix.Fields.Side.BUY)
                : new QuickFix.Fields.Side(QuickFix.Fields.Side.SELL);

            var response = await _fixClient.SendNewOrderSingleAndWaitAsync(
                order.Symbol,
                side,
                order.Quantity,
                order.Price,
                TimeSpan.FromSeconds(10));

            return new OrderResponseDto
            {
                IsAccepted = response.IsAccepted,
                ClOrdId = "N/A",
                RejectReason = response.RejectReason,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new OrderResponseDto
            {
                IsAccepted = false,
                RejectReason = ex.Message,
                Timestamp = DateTime.UtcNow
            };
        }
    }
}
