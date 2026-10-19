using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Data;

public sealed class ExpensesDbContext(
    DbContextOptions<ExpensesDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TransactionTag> TransactionTags => Set<TransactionTag>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletMember> WalletMembers => Set<WalletMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Goal>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Goal>()
            .HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Goal>()
            .Property(x => x.Currency)
            .HasMaxLength(3);

        modelBuilder.Entity<Goal>()
            .Property(x => x.Name)
            .HasMaxLength(100);

        modelBuilder.Entity<User>()
            .HasIndex(x => x.TelegramUserId)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(x => x.DefaultCurrency)
            .HasMaxLength(3);

        modelBuilder.Entity<Category>()
            .Property(x => x.Name)
            .HasMaxLength(80);

        modelBuilder.Entity<Transaction>()
            .Property(x => x.Currency)
            .HasMaxLength(3);

        modelBuilder.Entity<Transaction>()
            .Property(x => x.Note)
            .HasMaxLength(500);

        modelBuilder.Entity<User>()
            .HasMany(x => x.Categories)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasMany(x => x.Transactions)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Category>()
            .HasMany<Transaction>()
            .WithOne(x => x.Category)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(x => x.User)
            .WithMany(x => x.RecurringTransactions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTransaction>()
            .Property(x => x.Currency)
            .HasMaxLength(3);

        modelBuilder.Entity<RecurringTransaction>()
            .Property(x => x.Note)
            .HasMaxLength(500);

        modelBuilder.Entity<Budget>()
            .HasOne(x => x.User)
            .WithMany(x => x.Budgets)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Budget>()
            .HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Budget>()
            .Property(x => x.Currency)
            .HasMaxLength(3);

        modelBuilder.Entity<Transaction>()
            .HasIndex(x => new { x.UserId, x.OccurredAtUtc });

        modelBuilder.Entity<Transaction>()
            .Property(x => x.IdempotencyKey)
            .HasMaxLength(Transaction.MaxIdempotencyKeyLength);

        // SQLite treats NULLs as distinct, so only keyed transactions are constrained.
        modelBuilder.Entity<Transaction>()
            .HasIndex(x => new { x.UserId, x.IdempotencyKey })
            .IsUnique();

        modelBuilder.Entity<Tag>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tag>()
            .Property(x => x.Name)
            .HasMaxLength(50);

        modelBuilder.Entity<Tag>()
            .Property(x => x.Color)
            .HasMaxLength(7);

        modelBuilder.Entity<TransactionTag>()
            .HasKey(x => new { x.TransactionId, x.TagId });

        modelBuilder.Entity<NotificationLog>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NotificationLog>()
            .Property(x => x.Key)
            .HasMaxLength(64);

        // One row per (user, kind, subject): a second insert for the same
        // notification fails, which also protects against two Worker instances.
        modelBuilder.Entity<NotificationLog>()
            .HasIndex(x => new { x.UserId, x.Kind, x.Key })
            .IsUnique();

        modelBuilder.Entity<Wallet>()
            .Property(x => x.Name)
            .HasMaxLength(80);

        modelBuilder.Entity<Wallet>()
            .Property(x => x.InviteCode)
            .HasMaxLength(16);

        modelBuilder.Entity<Wallet>()
            .HasIndex(x => x.InviteCode)
            .IsUnique();

        modelBuilder.Entity<WalletMember>()
            .HasKey(x => new { x.WalletId, x.UserId });

        modelBuilder.Entity<WalletMember>()
            .HasOne(x => x.Wallet)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WalletMember>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a wallet keeps the members' transactions as personal ones.
        modelBuilder.Entity<Transaction>()
            .HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}