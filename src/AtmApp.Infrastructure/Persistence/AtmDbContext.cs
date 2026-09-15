using AtmApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Infrastructure.Persistence;

public class AtmDbContext : DbContext
{
    public AtmDbContext(DbContextOptions<AtmDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtmDbContext).Assembly);
    }
}
