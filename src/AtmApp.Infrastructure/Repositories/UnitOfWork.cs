using AtmApp.Domain.Repositories;
using AtmApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AtmDbContext _dbContext;

    public UnitOfWork(AtmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation)
    {
        // ExecutionStrategy wraps the transaction so transient failures (e.g. MySQL) are retried
        // as a whole unit rather than leaving a half-applied transfer — see §5 of the design doc.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            await operation();
            await transaction.CommitAsync();
        });
    }
}
