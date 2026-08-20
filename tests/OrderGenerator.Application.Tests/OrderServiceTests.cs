using OrderGenerator.Application.DTOs;
using OrderGenerator.Application.Services;
using Shared.Infrastructure.Fix;
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

public class OrderServiceTests
{
    [Fact]
    public async Task SendOrder_When_Connected_Returns_Accepted()
    {
        var mockClient = new MockFixClient
        {
            IsConnected = true,
            MockResponse = new FixResponse(true, null)
        };
        var service = new OrderService(mockClient);

        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var result = await service.SendOrderAsync(dto);

        Assert.True(result.IsAccepted);
        Assert.False(string.IsNullOrEmpty(result.ClOrdId));
    }

    [Fact]
    public async Task SendOrder_When_FixRejected_Returns_Rejected()
    {
        var mockClient = new MockFixClient
        {
            IsConnected = true,
            MockResponse = new FixResponse(false, "Exposure limit exceeded")
        };
        var service = new OrderService(mockClient);

        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var result = await service.SendOrderAsync(dto);

        Assert.False(result.IsAccepted);
        Assert.Equal("Exposure limit exceeded", result.RejectReason);
    }

    [Fact]
    public async Task SendOrder_When_Disconnected_Returns_Rejected()
    {
        var mockClient = new MockFixClient { IsConnected = false };
        var service = new OrderService(mockClient);

        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var result = await service.SendOrderAsync(dto);

        Assert.False(result.IsAccepted);
        Assert.Equal("FIX client not connected", result.RejectReason);
    }

    [Fact]
    public async Task SendOrder_Calls_FixClient_Send()
    {
        var mockClient = new MockFixClient { IsConnected = true };
        var service = new OrderService(mockClient);

        var dto = new OrderDto
        {
            Symbol = "VALE3",
            Side = "Venda",
            Quantity = 200,
            Price = 50.00m
        };

        await service.SendOrderAsync(dto);

        Assert.True(mockClient.SendCalled);
        Assert.Equal("VALE3", mockClient.LastSymbol);
        Assert.Equal(200, mockClient.LastQuantity);
        Assert.Equal(50.00m, mockClient.LastPrice);
    }

    [Fact]
    public async Task SendOrder_Returns_Timestamp()
    {
        var mockClient = new MockFixClient { IsConnected = true };
        var service = new OrderService(mockClient);

        var before = DateTime.UtcNow;
        var result = await service.SendOrderAsync(new OrderDto
        {
            Symbol = "VIIA4",
            Side = "Compra",
            Quantity = 10,
            Price = 5.00m
        });
        var after = DateTime.UtcNow;

        Assert.True(result.Timestamp >= before);
        Assert.True(result.Timestamp <= after);
    }
}
