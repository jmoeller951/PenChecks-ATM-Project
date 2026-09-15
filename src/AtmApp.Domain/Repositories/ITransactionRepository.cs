using AtmApp.Domain.Entities;

namespace AtmApp.Domain.Repositories;

public interface ITransactionRepository
{
    Task Add(Transaction transaction);
    Task<IReadOnlyList<Transaction>> GetByAccountId(Guid accountId);
    Task<Transaction?> GetByIdempotencyKey(Guid idempotencyKey);

    /// <summary>
    /// Most recent transaction for the account, ordered by Timestamp. Its BalanceAfter is the
    /// account's current balance (ledger pattern — see §2 of the design doc); avoids pulling the
    /// full history just to compute a balance.
    /// </summary>
    Task<Transaction?> GetLatestByAccountId(Guid accountId);
}
