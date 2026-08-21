using OrderGenerator.Application.DTOs;
using OrderGenerator.Application.Services;
using Shared.Infrastructure.Fix;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
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

public class OrderServiceTests : IDisposable
{
    private readonly SqliteEventStore _store;
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"baseorder-tests-{Guid.NewGuid():N}.db");
        var database = new SqliteDatabase(dbPath);
        _store = new SqliteEventStore(database);
        _service = new OrderService(new MockFixClient { IsConnected = true }, new MockEventBroker(), _store);
    }

    [Fact]
    public async Task SendOrder_Persists_Pending_Order_With_Outbox_Message()
    {
        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var result = await _service.SendOrderAsync(dto);

        Assert.False(string.IsNullOrEmpty(result.ClOrdId));

        var order = await _store.GetOrderAsync(result.ClOrdId!);
        Assert.NotNull(order);
        Assert.Equal("Pending", order!.Status);
        Assert.Equal("PETR4", order.Symbol);

        var outbox = await _store.GetUnpublishedOutboxAsync(10);
        var message = Assert.Single(outbox);
        Assert.Equal("orders.created", message.Topic);
        Assert.Contains(result.ClOrdId!, message.Payload);
    }

    [Fact]
    public async Task GetOrderStatus_Returns_Persisted_Order()
    {
        var dto = new OrderDto
        {
            Symbol = "VALE3",
            Side = "Venda",
            Quantity = 10,
            Price = 5.00m
        };

        var result = await _service.SendOrderAsync(dto);

        var status = await _service.GetOrderStatusAsync(result.ClOrdId!);

        Assert.NotNull(status);
        Assert.Equal("Pending", status!.Status);
        Assert.Equal("VALE3", status.Symbol);
        Assert.Equal(10, status.Quantity);
    }

    [Fact]
    public async Task GetOrderStatus_Returns_Null_For_Unknown_Order()
    {
        var status = await _service.GetOrderStatusAsync("nao-existe");
        Assert.Null(status);
    }

    public void Dispose()
    {
        var dbPath = Path.Combine(Path.GetTempPath());
        foreach (var file in Directory.GetFiles(dbPath, "baseorder-tests-*.db*"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
