using FluentValidation;

namespace Orders.Application.Orders;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public const int MaxTextLength = 150;
    public const decimal MaxValor = 9_999_999_999_999_999.99m;

    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Cliente)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Informe o cliente.")
            .MaximumLength(MaxTextLength).WithMessage($"O cliente deve ter no maximo {MaxTextLength} caracteres.");

        RuleFor(x => x.Produto)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Informe o produto.")
            .MaximumLength(MaxTextLength).WithMessage($"O produto deve ter no maximo {MaxTextLength} caracteres.");

        RuleFor(x => x.Valor)
            .GreaterThan(0).WithMessage("O valor deve ser maior que zero.")
            .LessThanOrEqualTo(MaxValor).WithMessage("O valor excede o limite permitido.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage("O valor deve ter no maximo 2 casas decimais.");
    }
}

public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    /// <summary>Evita overflow no calculo de OFFSET ((page - 1) * page_size).</summary>
    public const int MaxPage = 1_000_000;

    public ListOrdersQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage).WithMessage($"page deve estar entre 1 e {MaxPage}.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ListOrdersQuery.MaxPageSize)
            .WithMessage($"page_size deve estar entre 1 e {ListOrdersQuery.MaxPageSize}.");
    }
}
