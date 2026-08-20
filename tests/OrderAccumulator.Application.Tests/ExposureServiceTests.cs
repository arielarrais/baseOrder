using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Application.Services;
using OrderAccumulator.Domain.Enums;
using OrderAccumulator.Infrastructure.Persistence;
using Shared.Domain.ValueObjects;
using Xunit;

namespace OrderAccumulator.Application.Tests;

public class ExposureServiceTests
{
    private readonly ExposureService _service;

    public ExposureServiceTests()
    {
        var repository = new ExposureRepository();
        _service = new ExposureService(repository);
    }

    [Fact]
    public void GetCurrentExposure_Returns_Zero_Initially()
    {
        var exposure = _service.GetCurrentExposure(Symbol.PETR4);
        Assert.Equal(0m, exposure.Amount);
    }

    [Fact]
    public void GetLimit_Returns_100_Million()
    {
        var limit = _service.GetLimit();
        Assert.Equal(100_000_000m, limit.Amount);
    }

    [Fact]
    public void CanAcceptOrder_Within_Limit()
    {
        var orderValue = new Money(50_000_000);
        Assert.True(_service.CanAcceptOrder(Symbol.PETR4, orderValue));
    }

    [Fact]
    public void CanAcceptOrder_Exceeding_Limit()
    {
        var orderValue = new Money(100_000_001);
        Assert.False(_service.CanAcceptOrder(Symbol.PETR4, orderValue));
    }

    [Fact]
    public void UpdateExposure_Increases()
    {
        _service.UpdateExposure(Symbol.PETR4, new Money(1_000_000));
        Assert.Equal(1_000_000m, _service.GetCurrentExposure(Symbol.PETR4).Amount);
    }

    [Fact]
    public void GetAllExposures_Returns_All_Symbols()
    {
        var all = _service.GetAllExposures();
        Assert.Equal(3, all.Count);
    }
}
