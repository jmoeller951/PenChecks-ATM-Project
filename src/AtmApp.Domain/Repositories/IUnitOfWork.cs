namespace AtmApp.Domain.Repositories;

/// <summary>
/// Abstracts "run this as one atomic, all-or-nothing database transaction" so the Application
/// layer can demand transfer atomicity (§5) without referencing EF Core types directly.
/// </summary>
public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<Task> operation);
}
