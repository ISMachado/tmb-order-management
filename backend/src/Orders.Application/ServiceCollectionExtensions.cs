using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.Orders;
using Orders.Application.Processing;

namespace Orders.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>(includeInternalTypes: true);
        services.AddScoped<OrderService>();
        services.AddScoped<OrderProcessor>();
        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .Validate(o => o.DelaySeconds >= 0, "Processing:DelaySeconds deve ser >= 0.");
        return services;
    }
}
