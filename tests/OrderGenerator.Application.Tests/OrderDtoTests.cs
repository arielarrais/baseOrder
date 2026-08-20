using OrderGenerator.Application.DTOs;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace OrderGenerator.Application.Tests;

public class OrderDtoTests
{
    private List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Valid_Order_Dto_Has_No_Errors()
    {
        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var results = Validate(dto);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void OrderDto_Missing_Symbol_Is_Invalid(string? symbol)
    {
        var dto = new OrderDto
        {
            Symbol = symbol!,
            Side = "Compra",
            Quantity = 100,
            Price = 25.50m
        };

        var results = Validate(dto);
        Assert.Contains(results, r => r.MemberNames.Contains("Symbol"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void OrderDto_Missing_Side_Is_Invalid(string? side)
    {
        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = side!,
            Quantity = 100,
            Price = 25.50m
        };

        var results = Validate(dto);
        Assert.Contains(results, r => r.MemberNames.Contains("Side"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100000)]
    public void OrderDto_Invalid_Quantity_Is_Invalid(int quantity)
    {
        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = quantity,
            Price = 25.50m
        };

        var results = Validate(dto);
        Assert.Contains(results, r => r.MemberNames.Contains("Quantity"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void OrderDto_Invalid_Price_Is_Invalid(decimal price)
    {
        var dto = new OrderDto
        {
            Symbol = "PETR4",
            Side = "Compra",
            Quantity = 100,
            Price = price
        };

        var results = Validate(dto);
        Assert.Contains(results, r => r.MemberNames.Contains("Price"));
    }

    [Fact]
    public void OrderResponseDto_Accepted_Message()
    {
        var response = new OrderResponseDto
        {
            IsAccepted = true,
            ClOrdId = "CL001",
            Status = "Accepted"
        };

        Assert.Equal("Ordem Aceita", response.Message);
    }

    [Fact]
    public void OrderResponseDto_Rejected_Message()
    {
        var response = new OrderResponseDto
        {
            IsAccepted = false,
            ClOrdId = "CL001",
            RejectReason = "Exposure limit exceeded",
            Status = "Rejected"
        };

        Assert.Contains("Ordem Rejeitada", response.Message);
        Assert.Contains("Exposure limit exceeded", response.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99999)]
    public void OrderDto_Valid_Quantity_Boundary(int quantity)
    {
        var dto = new OrderDto
        {
            Symbol = "VALE3",
            Side = "Venda",
            Quantity = quantity,
            Price = 100.00m
        };

        var results = Validate(dto);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(999.99)]
    public void OrderDto_Valid_Price_Boundary(decimal price)
    {
        var dto = new OrderDto
        {
            Symbol = "VIIA4",
            Side = "Compra",
            Quantity = 10,
            Price = price
        };

        var results = Validate(dto);
        Assert.Empty(results);
    }
}
