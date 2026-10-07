using ExpenseHub.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Data;

internal sealed class ExpenseHubDbContext : IdentityDbContext<ApplicationUser>
{
    public ExpenseHubDbContext(DbContextOptions<ExpenseHubDbContext> options)
        : base(options)
    {
    }

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<ExpenseHistory> ExpenseHistories => Set<ExpenseHistory>();

    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ExpenseCategory>(entity =>
        {
            entity.ToTable("ExpenseCategories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name)
                .HasMaxLength(100)
                .IsRequired();
            entity.HasIndex(category => category.Name)
                .IsUnique();
        });

        builder.Entity<Expense>(entity =>
        {
            entity.ToTable("Expenses");
            entity.HasKey(expense => expense.Id);
            entity.Property(expense => expense.OwnerId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(expense => expense.Description)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(expense => expense.Amount)
                .HasColumnType("NUMBER(19,2)")
                .IsRequired();
            entity.Property(expense => expense.ExpenseDate)
                .HasColumnType("TIMESTAMP")
                .IsRequired();
            entity.Property(expense => expense.Status)
                .HasConversion<int>()
                .IsRequired();
            entity.HasIndex(expense => expense.OwnerId);
            entity.HasIndex(expense => expense.Status);
            entity.HasOne(expense => expense.Owner)
                .WithMany()
                .HasForeignKey(expense => expense.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(expense => expense.Category)
                .WithMany(category => category.Expenses)
                .HasForeignKey(expense => expense.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExpenseHistory>(entity =>
        {
            entity.ToTable("ExpenseHistories");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Action)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(history => history.ActorId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(history => history.TimestampUtc)
                .HasColumnType("TIMESTAMP")
                .IsRequired();
            entity.Property(history => history.PreviousStatus)
                .HasConversion<int>();
            entity.Property(history => history.NewStatus)
                .HasConversion<int>();
            entity.Property(history => history.RejectionReason)
                .HasMaxLength(500);
            entity.Property(history => history.DraftChanges)
                .HasColumnType("NCLOB");
            entity.HasIndex(history => new { history.ExpenseId, history.TimestampUtc });
            entity.HasOne(history => history.Expense)
                .WithMany(expense => expense.Histories)
                .HasForeignKey(history => history.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(history => history.Actor)
                .WithMany()
                .HasForeignKey(history => history.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PaymentRecord>(entity =>
        {
            entity.ToTable("PaymentRecords");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.PayerId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(payment => payment.PaidAtUtc)
                .HasColumnType("TIMESTAMP")
                .IsRequired();
            entity.HasIndex(payment => payment.ExpenseId)
                .IsUnique();
            entity.HasOne(payment => payment.Expense)
                .WithOne(expense => expense.PaymentRecord)
                .HasForeignKey<PaymentRecord>(payment => payment.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.Payer)
                .WithMany()
                .HasForeignKey(payment => payment.PayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
