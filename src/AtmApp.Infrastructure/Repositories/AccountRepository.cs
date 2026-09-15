using AtmApp.Domain.Entities;
using AtmApp.Domain.Repositories;
using AtmApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmApp.Infrastructure.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly AtmDbContext _dbContext;

    public AccountRepository(AtmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Account?> GetById(Guid id) =>
        await _dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

    public async Task<IReadOnlyList<Account>> GetAll() =>
        await _dbContext.Accounts.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync();
}
