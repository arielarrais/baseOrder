using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Domain.Entities;
using OrderAccumulator.Domain.Enums;
using OrderAccumulator.Domain.Exceptions;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Application.Handlers;

public class OrderHandler : IOrderHandler
{
    private readonly IExposureService _exposureService;

    public OrderHandler(IExposureService exposureService)
    {
        _exposureService = exposureService ?? throw new ArgumentNullException(nameof(exposureService));
    }

    public async Task<OrderResult> HandleNewOrderAsync(
        string clOrdId,
        Symbol symbol,
        Side side,
        int quantity,
        decimal price)
    {
        try
        {
            var priceMoney = new Money(price);
            var order = new Order(clOrdId, symbol, side, quantity, priceMoney);
            var orderExposure = order.CalculateExposure();

            if (!_exposureService.CanAcceptOrder(symbol, orderExposure))
            {
                order.Reject("Exposure limit exceeded");
                return new OrderResult
                {
                    IsAccepted = false,
                    ClOrdId = clOrdId,
                    RejectReason = "Exposure limit exceeded",
                    Timestamp = DateTime.Now
                };
            }

            _exposureService.UpdateExposure(symbol, orderExposure);
            order.Accept();

            return new OrderResult
            {
                IsAccepted = true,
                ClOrdId = clOrdId,
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            return new OrderResult
            {
                IsAccepted = false,
                ClOrdId = clOrdId,
                RejectReason = ex.Message,
                Timestamp = DateTime.Now
            };
        }
    }
}
