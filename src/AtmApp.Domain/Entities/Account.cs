namespace AtmApp.Domain.Entities;

public class Account
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; }
}
