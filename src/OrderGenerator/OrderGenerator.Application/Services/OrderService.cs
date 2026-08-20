using OrderGenerator.Application.DTOs;
using Polly;
using Polly.Retry;
using Shared.Infrastructure.Fix;

namespace OrderGenerator.Application.Services;

public class OrderService : IOrderService
{
    private readonly IFixClient _fixClient;
    private readonly ResiliencePipeline _retryPipeline;

    public OrderService(IFixClient fixClient)
    {
        _fixClient = fixClient ?? throw new ArgumentNullException(nameof(fixClient));

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

            var result = await _retryPipeline.ExecuteAsync(async ct =>
            {
                if (!_fixClient.IsConnected)
                    throw new InvalidOperationException("FIX session not connected");

                var side = order.Side == "Compra"
                    ? new QuickFix.Fields.Side(QuickFix.Fields.Side.BUY)
                    : new QuickFix.Fields.Side(QuickFix.Fields.Side.SELL);

                return await _fixClient.SendNewOrderSingleAndWaitAsync(
                    order.Symbol,
                    side,
                    order.Quantity,
                    order.Price,
                    TimeSpan.FromSeconds(10));
            });

            return new OrderResponseDto
            {
                IsAccepted = result.IsAccepted,
                ClOrdId = "N/A",
                RejectReason = result.RejectReason,
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
