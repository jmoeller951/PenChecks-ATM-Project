using AtmApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtmApp.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.BalanceAfter).HasColumnType("decimal(18,2)");
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);

        // Second line of defense against race conditions, alongside the application-level
        // idempotency check — see §4 of the design doc.
        builder.HasIndex(t => t.IdempotencyKey).IsUnique();

        builder.HasIndex(t => new { t.AccountId, t.Timestamp });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
