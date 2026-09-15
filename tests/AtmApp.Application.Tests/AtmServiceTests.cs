using AtmApp.Application.Common;
using AtmApp.Application.Services;
using AtmApp.Domain.Entities;
using AtmApp.Domain.Enums;
using AtmApp.Domain.Exceptions;
using AtmApp.Infrastructure.Persistence;
using AtmApp.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Application.Tests;

/// <summary>
/// Exercises AtmService against a real (in-memory) SQLite database via the actual repository
/// implementations, rather than mocks — the idempotency and transfer-atomicity guarantees are
/// properties of the Infrastructure + Application code working together.
/// </summary>
public class AtmServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AtmDbContext _dbContext;
    private readonly AtmService _sut;
    private readonly Account _checking;
    private readonly Account _savings;

    public AtmServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new AtmDbContext(options);
        _dbContext.Database.EnsureCreated();

        _checking = new Account { Id = Guid.NewGuid(), Name = "Checking", CreatedAt = DateTime.UtcNow };
        _savings = new Account { Id = Guid.NewGuid(), Name = "Savings", CreatedAt = DateTime.UtcNow };
        _dbContext.Accounts.AddRange(_checking, _savings);
        _dbContext.SaveChanges();

        var accountRepository = new AccountRepository(_dbContext);
        var transactionRepository = new TransactionRepository(_dbContext);
        var unitOfWork = new UnitOfWork(_dbContext);
        _sut = new AtmService(accountRepository, transactionRepository, unitOfWork);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Deposit_IncreasesBalance()
    {
        var result = await _sut.Deposit(_checking.Id, 100m, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, await _sut.GetBalance(_checking.Id));
    }

    [Fact]
    public async Task Deposit_ThenWithdraw_LeavesCorrectBalance()
    {
        await _sut.Deposit(_checking.Id, 100m, Guid.NewGuid());
        var result = await _sut.Withdraw(_checking.Id, 40m, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(60m, await _sut.GetBalance(_checking.Id));
    }

    [Fact]
    public async Task Withdraw_MoreThanBalance_ReturnsInsufficientFundsFailure()
    {
        await _sut.Deposit(_checking.Id, 50m, Guid.NewGuid());
        var result = await _sut.Withdraw(_checking.Id, 100m, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.InsufficientFunds, result.ErrorType);
        Assert.Equal(50m, await _sut.GetBalance(_checking.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Deposit_NonPositiveAmount_ReturnsInvalidAmountFailure(decimal amount)
    {
        var result = await _sut.Deposit(_checking.Id, amount, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.InvalidAmount, result.ErrorType);
    }

    [Fact]
    public async Task Deposit_UnknownAccount_ThrowsAccountNotFoundException()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _sut.Deposit(Guid.NewGuid(), 10m, Guid.NewGuid()));
    }

    [Fact]
    public async Task Deposit_ReplayedIdempotencyKey_DoesNotApplyTwice()
    {
        var key = Guid.NewGuid();

        var first = await _sut.Deposit(_checking.Id, 100m, key);
        var second = await _sut.Deposit(_checking.Id, 100m, key);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(100m, await _sut.GetBalance(_checking.Id));
    }

    [Fact]
    public async Task Transfer_MovesFundsBetweenAccounts()
    {
        await _sut.Deposit(_checking.Id, 200m, Guid.NewGuid());

        var result = await _sut.Transfer(_checking.Id, _savings.Id, 75m, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(125m, await _sut.GetBalance(_checking.Id));
        Assert.Equal(75m, await _sut.GetBalance(_savings.Id));

        var outgoing = result.Value!.OutgoingTransaction;
        var incoming = result.Value!.IncomingTransaction;
        Assert.Equal(TransactionType.TransferOut, outgoing.Type);
        Assert.Equal(TransactionType.TransferIn, incoming.Type);
        Assert.Equal(outgoing.Id, incoming.RelatedTransactionId);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_LeavesBothBalancesUnchanged()
    {
        await _sut.Deposit(_checking.Id, 10m, Guid.NewGuid());

        var result = await _sut.Transfer(_checking.Id, _savings.Id, 50m, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.InsufficientFunds, result.ErrorType);
        Assert.Equal(10m, await _sut.GetBalance(_checking.Id));
        Assert.Equal(0m, await _sut.GetBalance(_savings.Id));
    }

    [Fact]
    public async Task Transfer_ToSameAccount_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.Transfer(_checking.Id, _checking.Id, 10m, Guid.NewGuid()));
    }

    [Fact]
    public async Task Transfer_ReplayedIdempotencyKey_DoesNotApplyTwice()
    {
        await _sut.Deposit(_checking.Id, 200m, Guid.NewGuid());
        var key = Guid.NewGuid();

        var first = await _sut.Transfer(_checking.Id, _savings.Id, 75m, key);
        var second = await _sut.Transfer(_checking.Id, _savings.Id, 75m, key);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.OutgoingTransaction.Id, second.Value!.OutgoingTransaction.Id);
        Assert.Equal(125m, await _sut.GetBalance(_checking.Id));
        Assert.Equal(75m, await _sut.GetBalance(_savings.Id));
    }

    [Fact]
    public async Task GetHistory_ReturnsTransactionsMostRecentFirst()
    {
        await _sut.Deposit(_checking.Id, 100m, Guid.NewGuid());
        await _sut.Withdraw(_checking.Id, 30m, Guid.NewGuid());

        var history = await _sut.GetHistory(_checking.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal(TransactionType.Withdrawal, history[0].Type);
        Assert.Equal(TransactionType.Deposit, history[1].Type);
    }
}
