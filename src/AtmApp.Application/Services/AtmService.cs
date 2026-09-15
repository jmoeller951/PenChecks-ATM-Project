using AtmApp.Application.Common;
using AtmApp.Application.DTOs;
using AtmApp.Domain.Entities;
using AtmApp.Domain.Enums;
using AtmApp.Domain.Exceptions;
using AtmApp.Domain.Repositories;

namespace AtmApp.Application.Services;

public class AtmService : IAtmService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AtmService(
        IAccountRepository accountRepository,
        ITransactionRepository transactionRepository,
        IUnitOfWork unitOfWork)
    {
        _accountRepository = accountRepository;
        _transactionRepository = transactionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<Account>> GetAccounts() => await _accountRepository.GetAll();

    public async Task<Result<Transaction>> Deposit(Guid accountId, decimal amount, Guid idempotencyKey)
    {
        if (await _transactionRepository.GetByIdempotencyKey(idempotencyKey) is { } existing)
            return Result<Transaction>.Success(existing);

        await GetAccountOrThrow(accountId);

        if (amount <= 0)
            return Result<Transaction>.Failure(new InvalidAmountException(amount).Message, ErrorType.InvalidAmount);

        var currentBalance = await GetBalance(accountId);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = TransactionType.Deposit,
            Amount = amount,
            BalanceAfter = currentBalance + amount,
            IdempotencyKey = idempotencyKey,
            Timestamp = DateTime.UtcNow
        };

        await _transactionRepository.Add(transaction);
        return Result<Transaction>.Success(transaction);
    }

    public async Task<Result<Transaction>> Withdraw(Guid accountId, decimal amount, Guid idempotencyKey)
    {
        if (await _transactionRepository.GetByIdempotencyKey(idempotencyKey) is { } existing)
            return Result<Transaction>.Success(existing);

        var account = await GetAccountOrThrow(accountId);

        if (amount <= 0)
            return Result<Transaction>.Failure(new InvalidAmountException(amount).Message, ErrorType.InvalidAmount);

        var currentBalance = await GetBalance(accountId);

        if (amount > currentBalance)
        {
            return Result<Transaction>.Failure(
                new InsufficientFundsException(accountId, account.Name, amount, currentBalance).Message,
                ErrorType.InsufficientFunds);
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = TransactionType.Withdrawal,
            Amount = amount,
            BalanceAfter = currentBalance - amount,
            IdempotencyKey = idempotencyKey,
            Timestamp = DateTime.UtcNow
        };

        await _transactionRepository.Add(transaction);
        return Result<Transaction>.Success(transaction);
    }

    public async Task<Result<TransferResult>> Transfer(Guid fromAccountId, Guid toAccountId, decimal amount, Guid idempotencyKey)
    {
        if (fromAccountId == toAccountId)
            throw new ArgumentException("Cannot transfer to the same account.", nameof(toAccountId));

        if (await _transactionRepository.GetByIdempotencyKey(idempotencyKey) is { } existingOut)
        {
            // The two legs of a transfer share amount/timestamp; the incoming leg is linked via RelatedTransactionId.
            var existingIn = (await _transactionRepository.GetByAccountId(toAccountId))
                .Single(t => t.RelatedTransactionId == existingOut.Id);
            return Result<TransferResult>.Success(new TransferResult(existingOut, existingIn));
        }

        var fromAccount = await GetAccountOrThrow(fromAccountId);
        await GetAccountOrThrow(toAccountId);

        if (amount <= 0)
            return Result<TransferResult>.Failure(new InvalidAmountException(amount).Message, ErrorType.InvalidAmount);

        Result<TransferResult>? failure = null;
        TransferResult? success = null;

        // Always lock/touch accounts in a consistent order (by Id) to avoid deadlocks between
        // concurrent transfers running in opposite directions — see §5 of the design doc.
        var (firstId, secondId) = fromAccountId.CompareTo(toAccountId) < 0
            ? (fromAccountId, toAccountId)
            : (toAccountId, fromAccountId);

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var firstBalance = await GetBalance(firstId);
            var secondBalance = await GetBalance(secondId);
            var fromBalance = firstId == fromAccountId ? firstBalance : secondBalance;
            var toBalance = firstId == fromAccountId ? secondBalance : firstBalance;

            if (amount > fromBalance)
            {
                failure = Result<TransferResult>.Failure(
                    new InsufficientFundsException(fromAccountId, fromAccount.Name, amount, fromBalance).Message,
                    ErrorType.InsufficientFunds);
                return;
            }

            var now = DateTime.UtcNow;

            var outgoing = new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = fromAccountId,
                Type = TransactionType.TransferOut,
                Amount = amount,
                BalanceAfter = fromBalance - amount,
                IdempotencyKey = idempotencyKey,
                Timestamp = now
            };
            await _transactionRepository.Add(outgoing);

            var incoming = new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = toAccountId,
                Type = TransactionType.TransferIn,
                Amount = amount,
                BalanceAfter = toBalance + amount,
                RelatedTransactionId = outgoing.Id,
                IdempotencyKey = Guid.NewGuid(),
                Timestamp = now
            };
            await _transactionRepository.Add(incoming);

            success = new TransferResult(outgoing, incoming);
        });

        return failure ?? Result<TransferResult>.Success(success!);
    }

    public async Task<IReadOnlyList<Transaction>> GetHistory(Guid accountId)
    {
        await GetAccountOrThrow(accountId);
        return await _transactionRepository.GetByAccountId(accountId);
    }

    public async Task<decimal> GetBalance(Guid accountId)
    {
        var latest = await _transactionRepository.GetLatestByAccountId(accountId);
        return latest?.BalanceAfter ?? 0m;
    }

    private async Task<Account> GetAccountOrThrow(Guid accountId) =>
        await _accountRepository.GetById(accountId) ?? throw new AccountNotFoundException(accountId);
}
