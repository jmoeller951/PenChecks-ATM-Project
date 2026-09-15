using AtmApp.Domain.Entities;

namespace AtmApp.Domain.Repositories;

public interface IAccountRepository
{
    Task<Account?> GetById(Guid id);
    Task<IReadOnlyList<Account>> GetAll();
}
