using AtmApp.Application.Common;
using AtmApp.Application.DTOs;
using AtmApp.Domain.Entities;

namespace AtmApp.Application.Services;

public interface IAtmService
{
    Task<IReadOnlyList<Account>> GetAccounts();
    Task<Result<Transaction>> Deposit(Guid accountId, decimal amount, Guid idempotencyKey);
    Task<Result<Transaction>> Withdraw(Guid accountId, decimal amount, Guid idempotencyKey);
    Task<Result<TransferResult>> Transfer(Guid fromAccountId, Guid toAccountId, decimal amount, Guid idempotencyKey);
    Task<IReadOnlyList<Transaction>> GetHistory(Guid accountId);
    Task<decimal> GetBalance(Guid accountId);
}
