using OrderGenerator.Application.DTOs;
using OrderGenerator.Application.Services;
using Shared.Infrastructure.Fix;
using Shared.Infrastructure.Messaging;
using Xunit;

namespace OrderGenerator.Application.Tests;

public class MockFixClient : IFixClient
{
    public bool IsConnected { get; set; }
    public bool SendCalled { get; private set; }
    public string? LastSymbol { get; private set; }
    public int LastQuantity { get; private set; }
    public decimal LastPrice { get; private set; }
    public FixResponse? MockResponse { get; set; }

    public void Connect() => IsConnected = true;
    public void Disconnect() => IsConnected = false;

    public Task<FixResponse> SendNewOrderSingleAndWaitAsync(string symbol, QuickFix.Fields.Side side, int quantity, decimal price, TimeSpan timeout)
    {
        SendCalled = true;
        LastSymbol = symbol;
        LastQuantity = quantity;
        LastPrice = price;
        var response = MockResponse ?? new FixResponse(true, null);
        return Task.FromResult(response);
    }

    public void Dispose() { }
}

public class MockEventBroker : IEventBroker
{
    public bool PublishCalled { get; private set; }
    public string? LastTopic { get; private set; }
    public object? LastEvent { get; private set; }

    public Task PublishAsync<T>(string topic, T evt) where T : class
    {
        PublishCalled = true;
        LastTopic = topic;
        LastEvent = evt;
        return Task.CompletedTask;
    }

    public Task<T?> ConsumeAsync<T>(string topic, TimeSpan timeout) where T : class
    {
        return Task.FromResult<T?>(null);
    }

    public void Subscribe<T>(string topic, Func<T, Task> handler) where T : class { }

    public void StartConsuming<T>(string topic, Func<T, Task> handler, System.Threading.CancellationToken ct) where T : class { }
}

public class OrderServiceTests
{
    [Fact]
    public async Task SendOrder_When_Connected_Publishes_Event()
    {
        var mockClient = new MockFixClient { IsConnected = true };
        var mockBroker = new MockEventBroker();
        var service = new OrderService(mockClient, mockBroker);

        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var result = await service.SendOrderAsync(dto);

        Assert.True(mockBroker.PublishCalled);
        Assert.Equal("orders.created", mockBroker.LastTopic);
        Assert.False(string.IsNullOrEmpty(result.ClOrdId));
    }

    [Fact]
    public async Task SendOrder_Returns_Pending_Status()
    {
        var mockClient = new MockFixClient { IsConnected = true };
        var mockBroker = new MockEventBroker();
        var service = new OrderService(mockClient, mockBroker);

        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var result = await service.SendOrderAsync(dto);

        Assert.False(result.IsAccepted);
        Assert.False(string.IsNullOrEmpty(result.ClOrdId));
    }

    [Fact]
    public async Task SendOrder_Returns_Timestamp()
    {
        var mockClient = new MockFixClient { IsConnected = true };
        var mockBroker = new MockEventBroker();
        var service = new OrderService(mockClient, mockBroker);

        var before = DateTime.Now;
        var result = await service.SendOrderAsync(new OrderDto
        {
            Symbol = "VIIA4",
            Side = "Compra",
            Quantity = 10,
            Price = 5.00m
        });
        var after = DateTime.Now;

        Assert.True(result.Timestamp >= before);
        Assert.True(result.Timestamp <= after);
    }
}
