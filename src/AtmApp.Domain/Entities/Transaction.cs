using AtmApp.Domain.Enums;

namespace AtmApp.Domain.Entities;

public class Transaction
{
    public Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required TransactionType Type { get; init; }
    public required decimal Amount { get; init; }
    public required decimal BalanceAfter { get; init; }
    public Guid? RelatedTransactionId { get; init; }
    public required Guid IdempotencyKey { get; init; }
    public DateTime Timestamp { get; init; }
}
