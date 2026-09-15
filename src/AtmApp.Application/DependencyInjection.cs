using AtmApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AtmApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAtmService, AtmService>();
        return services;
    }
}
