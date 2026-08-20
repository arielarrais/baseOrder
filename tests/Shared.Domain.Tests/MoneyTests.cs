using Shared.Domain.ValueObjects;
using Xunit;

namespace Shared.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Create_Money_With_Valid_Amount()
    {
        var money = new Money(100.50m);
        Assert.Equal(100.50m, money.Amount);
        Assert.Equal("BRL", money.Currency);
    }

    [Fact]
    public void Create_Money_With_Zero_Amount()
    {
        var money = new Money(0);
        Assert.Equal(0m, money.Amount);
    }

    [Fact]
    public void Create_Money_With_Negative_Amount()
    {
        var money = new Money(-100);
        Assert.Equal(-100m, money.Amount);
    }

    [Fact]
    public void Money_Zero_Returns_Zero_Amount()
    {
        var money = Money.Zero();
        Assert.Equal(0m, money.Amount);
        Assert.Equal("BRL", money.Currency);
    }

    [Fact]
    public void Money_Add_Same_Currency()
    {
        var left = new Money(100);
        var right = new Money(50);
        var result = left + right;
        Assert.Equal(150m, result.Amount);
    }

    [Fact]
    public void Money_Subtract_Same_Currency()
    {
        var left = new Money(100);
        var right = new Money(30);
        var result = left - right;
        Assert.Equal(70m, result.Amount);
    }

    [Fact]
    public void Money_Add_Different_Currencies_Throws()
    {
        var left = new Money(100, "BRL");
        var right = new Money(50, "USD");
        Assert.Throws<InvalidOperationException>(() => left + right);
    }

    [Fact]
    public void Money_Multiply_By_Int()
    {
        var money = new Money(10);
        var result = money * 5;
        Assert.Equal(50m, result.Amount);
    }

    [Fact]
    public void Money_Multiply_By_Decimal()
    {
        var money = new Money(10);
        var result = money * 2.5m;
        Assert.Equal(25m, result.Amount);
    }

    [Fact]
    public void Money_IsGreaterThan()
    {
        var left = new Money(200);
        var right = new Money(100);
        Assert.True(left.IsGreaterThan(right));
        Assert.False(right.IsGreaterThan(left));
    }

    [Fact]
    public void Money_IsLessThan()
    {
        var left = new Money(50);
        var right = new Money(100);
        Assert.True(left.IsLessThan(right));
    }

    [Fact]
    public void Money_Abs_Returns_Absolute_Value()
    {
        var positive = new Money(100);
        var abs = positive.Abs();
        Assert.Equal(100m, abs.Amount);
    }

    [Fact]
    public void Money_Rounds_To_2_Decimal_Places()
    {
        var money = new Money(100.456m);
        Assert.Equal(100.46m, money.Amount);
    }

    [Fact]
    public void Money_Equality()
    {
        var left = new Money(100);
        var right = new Money(100);
        Assert.Equal(left, right);
    }

    [Fact]
    public void Money_ToString_Formats_Correctly()
    {
        var money = new Money(100.50m);
        Assert.Equal("100.50 BRL", money.ToString());
    }
}
