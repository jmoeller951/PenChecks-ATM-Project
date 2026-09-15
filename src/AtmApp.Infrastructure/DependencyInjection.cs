using AtmApp.Domain.Repositories;
using AtmApp.Infrastructure.Persistence;
using AtmApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AtmApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString(provider)
            ?? throw new InvalidOperationException($"No connection string configured for provider '{provider}'.");

        services.AddDbContext<AtmDbContext>(options =>
        {
            // Provider selection lives entirely here — swapping SQLite for MySQL is a config
            // change, not a code change. See §6 of the design doc.
            if (provider == "MySql")
                options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            else
                options.UseSqlite(connectionString);
        });

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
