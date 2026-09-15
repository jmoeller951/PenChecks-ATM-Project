using AtmApp.Domain.Entities;
using AtmApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Infrastructure.Seed;

/// <summary>
/// Seeds the two demo accounts there's no account-creation flow for — see §6 of the design doc.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AtmDbContext dbContext)
    {
        await dbContext.Database.MigrateAsync();

        if (await dbContext.Accounts.AnyAsync())
            return;

        var now = DateTime.UtcNow;
        dbContext.Accounts.AddRange(
            new Account { Id = Guid.NewGuid(), Name = "Checking", CreatedAt = now },
            new Account { Id = Guid.NewGuid(), Name = "Savings", CreatedAt = now });

        await dbContext.SaveChangesAsync();
    }
}
