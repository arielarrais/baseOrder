using OrderAccumulator.Domain.Entities;
using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;
using Xunit;

namespace OrderAccumulator.Domain.Tests;

public class ExposureTests
{
    private readonly Exposure _exposure;

    public ExposureTests()
    {
        _exposure = new Exposure();
    }

    [Fact]
    public void Initial_Exposure_Is_Zero_For_All_Symbols()
    {
        Assert.Equal(0m, _exposure.GetCurrentExposure(Symbol.PETR4).Amount);
        Assert.Equal(0m, _exposure.GetCurrentExposure(Symbol.VALE3).Amount);
        Assert.Equal(0m, _exposure.GetCurrentExposure(Symbol.VIIA4).Amount);
    }

    [Fact]
    public void GetLimit_Returns_100_Million()
    {
        var limit = _exposure.GetLimit();
        Assert.Equal(100_000_000m, limit.Amount);
    }

    [Fact]
    public void CanAcceptOrder_Within_Limit_Returns_True()
    {
        var orderValue = new Money(50_000_000);
        Assert.True(_exposure.CanAcceptOrder(Symbol.PETR4, orderValue));
    }

    [Fact]
    public void CanAcceptOrder_Exceeding_Limit_Returns_False()
    {
        var orderValue = new Money(100_000_001);
        Assert.False(_exposure.CanAcceptOrder(Symbol.PETR4, orderValue));
    }

    [Fact]
    public void CanAcceptOrder_At_Limit_Returns_True()
    {
        var orderValue = new Money(100_000_000);
        Assert.True(_exposure.CanAcceptOrder(Symbol.PETR4, orderValue));
    }

    [Fact]
    public void UpdateExposure_Increases_For_Buy()
    {
        _exposure.UpdateExposure(Symbol.PETR4, new Money(1_000_000));
        Assert.Equal(1_000_000m, _exposure.GetCurrentExposure(Symbol.PETR4).Amount);
    }

    [Fact]
    public void UpdateExposure_Decreases_For_Sell()
    {
        _exposure.UpdateExposure(Symbol.PETR4, new Money(5_000_000));
        _exposure.UpdateExposure(Symbol.PETR4, new Money(-2_000_000));
        Assert.Equal(3_000_000m, _exposure.GetCurrentExposure(Symbol.PETR4).Amount);
    }

    [Fact]
    public void UpdateExposure_Can_Go_Negative()
    {
        _exposure.UpdateExposure(Symbol.VALE3, new Money(-50_000_000));
        Assert.Equal(-50_000_000m, _exposure.GetCurrentExposure(Symbol.VALE3).Amount);
    }

    [Fact]
    public void Exposure_Is_Tracked_Per_Symbol()
    {
        _exposure.UpdateExposure(Symbol.PETR4, new Money(1_000_000));
        _exposure.UpdateExposure(Symbol.VALE3, new Money(2_000_000));

        Assert.Equal(1_000_000m, _exposure.GetCurrentExposure(Symbol.PETR4).Amount);
        Assert.Equal(2_000_000m, _exposure.GetCurrentExposure(Symbol.VALE3).Amount);
    }

    [Fact]
    public void GetAllExposures_Returns_All_Symbols()
    {
        var all = _exposure.GetAllExposures();
        Assert.Equal(3, all.Count);
        Assert.True(all.ContainsKey(Symbol.PETR4));
        Assert.True(all.ContainsKey(Symbol.VALE3));
        Assert.True(all.ContainsKey(Symbol.VIIA4));
    }

    [Fact]
    public void Multiple_Orders_Cumulative_Exposure()
    {
        _exposure.UpdateExposure(Symbol.PETR4, new Money(10_000_000));
        _exposure.UpdateExposure(Symbol.PETR4, new Money(5_000_000));
        _exposure.UpdateExposure(Symbol.PETR4, new Money(-2_000_000));

        Assert.Equal(13_000_000m, _exposure.GetCurrentExposure(Symbol.PETR4).Amount);
    }

    [Fact]
    public void Near_Limit_Blocks_Subsequent_Order()
    {
        _exposure.UpdateExposure(Symbol.PETR4, new Money(99_000_000));
        var nextOrder = new Money(1_500_000);

        Assert.False(_exposure.CanAcceptOrder(Symbol.PETR4, nextOrder));
    }
}
