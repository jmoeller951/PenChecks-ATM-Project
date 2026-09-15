using AtmApp.Domain.Entities;
using AtmApp.Domain.Repositories;
using AtmApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Infrastructure.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly AtmDbContext _dbContext;

    public TransactionRepository(AtmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Add(Transaction transaction)
    {
        _dbContext.Transactions.Add(transaction);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Transaction>> GetByAccountId(Guid accountId) =>
        await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();

    public async Task<Transaction?> GetByIdempotencyKey(Guid idempotencyKey) =>
        await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey);

    public async Task<Transaction?> GetLatestByAccountId(Guid accountId) =>
        await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync();
}
