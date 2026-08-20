using OrderAccumulator.Domain.Entities;
using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;
using Xunit;

namespace OrderAccumulator.Domain.Tests;

public class OrderTests
{
    [Fact]
    public void Create_Order_With_Valid_Parameters()
    {
        var order = new Order("CL001", Symbol.PETR4, Side.Buy, 100, new Money(25.50m));

        Assert.Equal("CL001", order.ClOrdId);
        Assert.Equal(Symbol.PETR4, order.Symbol);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(100, order.Quantity);
        Assert.Equal(25.50m, order.Price.Amount);
        Assert.Equal(OrderStatus.New, order.Status);
    }

    [Fact]
    public void Create_Order_With_Null_ClOrdId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Order(null!, Symbol.PETR4, Side.Buy, 100, new Money(25)));
    }

    [Fact]
    public void Create_Order_With_Zero_Quantity_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Order("CL001", Symbol.PETR4, Side.Buy, 0, new Money(25)));
    }

    [Fact]
    public void Create_Order_With_Negative_Quantity_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Order("CL001", Symbol.PETR4, Side.Buy, -10, new Money(25)));
    }

    [Fact]
    public void CalculateExposure_Buy_Returns_Positive()
    {
        var order = new Order("CL001", Symbol.PETR4, Side.Buy, 100, new Money(10));
        var exposure = order.CalculateExposure();

        Assert.Equal(1000m, exposure.Amount);
    }

    [Fact]
    public void CalculateExposure_Sell_Returns_Negative()
    {
        var order = new Order("CL001", Symbol.VALE3, Side.Sell, 50, new Money(20));
        var exposure = order.CalculateExposure();

        Assert.Equal(-1000m, exposure.Amount);
    }

    [Fact]
    public void Order_Accept_Changes_Status_To_Filled()
    {
        var order = new Order("CL001", Symbol.PETR4, Side.Buy, 100, new Money(25));
        order.Accept();

        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public void Order_Reject_Changes_Status_To_Rejected()
    {
        var order = new Order("CL001", Symbol.PETR4, Side.Buy, 100, new Money(25));
        order.Reject("Exposure limit exceeded");

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Exposure limit exceeded", order.RejectReason);
    }

    [Fact]
    public void Order_Has_Unique_Id()
    {
        var order1 = new Order("CL001", Symbol.PETR4, Side.Buy, 100, new Money(25));
        var order2 = new Order("CL002", Symbol.VALE3, Side.Sell, 50, new Money(30));

        Assert.NotEqual(order1.Id, order2.Id);
    }
}
