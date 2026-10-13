using Orders.Application.Orders;

namespace Orders.UnitTests;

public class ValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes() =>
        Assert.True(_validator.Validate(new CreateOrderRequest("Ana", "Notebook", 1299.90m)).IsValid);

    [Theory]
    [InlineData("", "p", 1, "Cliente")]
    [InlineData("   ", "p", 1, "Cliente")]
    [InlineData("c", "", 1, "Produto")]
    [InlineData("c", "p", 0, "Valor")]
    [InlineData("c", "p", -10, "Valor")]
    [InlineData("c", "p", 10.123, "Valor")]
    public void Invalid_requests_fail_on_the_right_field(string cliente, string produto, double valor, string property)
    {
        var result = _validator.Validate(new CreateOrderRequest(cliente, produto, (decimal)valor));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }

    [Fact]
    public void Texts_longer_than_150_characters_are_rejected()
    {
        var long151 = new string('x', 151);

        Assert.False(_validator.Validate(new CreateOrderRequest(long151, "p", 1)).IsValid);
        Assert.False(_validator.Validate(new CreateOrderRequest("c", long151, 1)).IsValid);
        Assert.True(_validator.Validate(new CreateOrderRequest(new string('x', 150), "p", 1)).IsValid);
    }

    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 201, false)]
    [InlineData(1, 200, true)]
    [InlineData(3, 10, true)]
    public void Pagination_limits_are_enforced(int page, int pageSize, bool valid) =>
        Assert.Equal(valid, new ListOrdersQueryValidator().Validate(new ListOrdersQuery(null, page, pageSize)).IsValid);
}
