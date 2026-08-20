using OrderAccumulator.Application.Handlers;
using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Application.Services;
using OrderAccumulator.Domain.Enums;
using OrderAccumulator.Infrastructure.Persistence;
using Xunit;

namespace OrderAccumulator.Application.Tests;

public class OrderHandlerTests
{
    private readonly OrderHandler _handler;
    private readonly ExposureRepository _repository;

    public OrderHandlerTests()
    {
        _repository = new ExposureRepository();
        var exposureService = new ExposureService(_repository);
        _handler = new OrderHandler(exposureService);
    }

    [Fact]
    public async Task Handle_Buy_Order_Accepted()
    {
        var result = await _handler.HandleNewOrderAsync(
            "CL001", Symbol.PETR4, Side.Buy, 100, 25.50m);

        Assert.True(result.IsAccepted);
        Assert.Equal("CL001", result.ClOrdId);
        Assert.Null(result.RejectReason);
    }

    [Fact]
    public async Task Handle_Sell_Order_Accepted()
    {
        var result = await _handler.HandleNewOrderAsync(
            "CL002", Symbol.VALE3, Side.Sell, 50, 30.00m);

        Assert.True(result.IsAccepted);
        Assert.Equal("CL002", result.ClOrdId);
    }

    [Fact]
    public async Task Handle_Order_Updates_Exposure()
    {
        await _handler.HandleNewOrderAsync(
            "CL001", Symbol.PETR4, Side.Buy, 100, 10.00m);

        var exposure = _repository.GetCurrentExposure(Symbol.PETR4);
        Assert.Equal(1000m, exposure.Amount);
    }

    [Fact]
    public async Task Handle_Multiple_Orders_Cumulative_Exposure()
    {
        await _handler.HandleNewOrderAsync("CL001", Symbol.PETR4, Side.Buy, 100, 10.00m);
        await _handler.HandleNewOrderAsync("CL002", Symbol.PETR4, Side.Buy, 50, 20.00m);

        var exposure = _repository.GetCurrentExposure(Symbol.PETR4);
        Assert.Equal(2000m, exposure.Amount);
    }

    [Fact]
    public async Task Handle_Order_Exceeding_Limit_Rejected()
    {
        var result = await _handler.HandleNewOrderAsync(
            "CL001", Symbol.PETR4, Side.Buy, 100_000, 1001.00m);

        Assert.False(result.IsAccepted);
        Assert.Equal("Exposure limit exceeded", result.RejectReason);
    }

    [Fact]
    public async Task Handle_Rejected_Order_Does_Not_Update_Exposure()
    {
        await _handler.HandleNewOrderAsync(
            "CL001", Symbol.PETR4, Side.Buy, 100_000, 1001.00m);

        var exposure = _repository.GetCurrentExposure(Symbol.PETR4);
        Assert.Equal(0m, exposure.Amount);
    }

    [Fact]
    public async Task Handle_Order_At_Limit_Accepted()
    {
        var result = await _handler.HandleNewOrderAsync(
            "CL001", Symbol.PETR4, Side.Buy, 100_000, 1000.00m);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public async Task Handle_Buy_Then_Sell_Updates_Correctly()
    {
        await _handler.HandleNewOrderAsync("CL001", Symbol.VALE3, Side.Buy, 100, 100.00m);
        await _handler.HandleNewOrderAsync("CL002", Symbol.VALE3, Side.Sell, 50, 100.00m);

        var exposure = _repository.GetCurrentExposure(Symbol.VALE3);
        Assert.Equal(5000m, exposure.Amount);
    }

    [Fact]
    public async Task Handle_Order_Returns_Timestamp()
    {
        var before = DateTime.Now;
        var result = await _handler.HandleNewOrderAsync(
            "CL001", Symbol.PETR4, Side.Buy, 10, 50.00m);
        var after = DateTime.Now;

        Assert.True(result.Timestamp >= before);
        Assert.True(result.Timestamp <= after);
    }

    [Fact]
    public async Task Handle_Independent_Symbols()
    {
        await _handler.HandleNewOrderAsync("CL001", Symbol.PETR4, Side.Buy, 100, 10.00m);
        await _handler.HandleNewOrderAsync("CL002", Symbol.VALE3, Side.Buy, 200, 5.00m);

        Assert.Equal(1000m, _repository.GetCurrentExposure(Symbol.PETR4).Amount);
        Assert.Equal(1000m, _repository.GetCurrentExposure(Symbol.VALE3).Amount);
    }
}
