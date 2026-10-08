using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExpenseHub.Api.Expenses;

internal sealed class EfExpenseDraftRepository : IExpenseDraftRepository, IExpenseQueryRepository
{
    private readonly ExpenseHubDbContext _dbContext;
    private IDbContextTransaction? _transaction;

    public EfExpenseDraftRepository(ExpenseHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Expense?> FindOwnedAsync(int id, string ownerId, CancellationToken cancellationToken) => _dbContext.Expenses
        .Include(expense => expense.Category)
        .Include(expense => expense.Histories)
        .SingleOrDefaultAsync(expense => expense.Id == id && expense.OwnerId == ownerId, cancellationToken);

    public Task<Expense?> FindOwnedReadOnlyAsync(int id, string ownerId, CancellationToken cancellationToken) => _dbContext.Expenses
        .AsNoTracking()
        .Include(expense => expense.Category)
        .SingleOrDefaultAsync(expense => expense.Id == id && expense.OwnerId == ownerId, cancellationToken);

    public async Task<Expense?> SubmitOwnedAsync(int id, string ownerId, DateTime submittedAtUtc, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        int updated = await _dbContext.Expenses
            .Where(expense => expense.Id == id && expense.OwnerId == ownerId && expense.Status == ExpenseStatus.Draft)
            .ExecuteUpdateAsync(setters => setters.SetProperty(expense => expense.Status, ExpenseStatus.Submitted), cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Expense? expense = await _dbContext.Expenses
            .AsNoTracking()
            .Include(item => item.Category)
            .SingleAsync(item => item.Id == id, cancellationToken);
        _dbContext.ExpenseHistories.Add(new ExpenseHistory
        {
            ExpenseId = id,
            ActorId = ownerId,
            Action = "Submitted",
            TimestampUtc = submittedAtUtc,
            PreviousStatus = ExpenseStatus.Draft,
            NewStatus = ExpenseStatus.Submitted
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expense;
    }

    public Task<ExpenseCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken) => _dbContext.ExpenseCategories
        .SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    public Task<Expense?> FindByIdAsync(int id, CancellationToken cancellationToken) => _dbContext.Expenses
        .AsNoTracking()
        .Include(expense => expense.Category)
        .SingleOrDefaultAsync(expense => expense.Id == id, cancellationToken);

    public Task<Expense?> FindByIdReadOnlyAsync(int id, CancellationToken cancellationToken) => _dbContext.Expenses
        .AsNoTracking()
        .Include(expense => expense.Category)
        .SingleOrDefaultAsync(expense => expense.Id == id, cancellationToken);

    public async Task<Expense?> DecideAsync(int id, string actorId, ExpenseStatus targetStatus, string? justification, DateTime timestampUtc, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        int updated = await _dbContext.Expenses
            .Where(expense => expense.Id == id && expense.Status == ExpenseStatus.Submitted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(expense => expense.Status, targetStatus), cancellationToken);

        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        ExpenseStatus previousStatus = ExpenseStatus.Submitted;
        Expense? expense = await _dbContext.Expenses
            .AsNoTracking()
            .Include(item => item.Category)
            .SingleAsync(item => item.Id == id, cancellationToken);

        _dbContext.ExpenseHistories.Add(new ExpenseHistory
        {
            ExpenseId = id,
            ActorId = actorId,
            Action = targetStatus == ExpenseStatus.Approved ? "Approved" : "Rejected",
            TimestampUtc = timestampUtc,
            PreviousStatus = previousStatus,
            NewStatus = targetStatus,
            RejectionReason = targetStatus == ExpenseStatus.Rejected ? justification : null
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expense;
    }

    public async Task<Expense?> PayAsync(int id, string actorId, DateTime timestampUtc, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        int updated = await _dbContext.Expenses
            .Where(expense => expense.Id == id && expense.Status == ExpenseStatus.Approved)
            .ExecuteUpdateAsync(setters => setters.SetProperty(expense => expense.Status, ExpenseStatus.Paid), cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Expense expense = await _dbContext.Expenses.AsNoTracking().Include(item => item.Category).SingleAsync(item => item.Id == id, cancellationToken);
        _dbContext.PaymentRecords.Add(new PaymentRecord { ExpenseId = id, PayerId = actorId, PaidAtUtc = timestampUtc });
        _dbContext.ExpenseHistories.Add(new ExpenseHistory { ExpenseId = id, ActorId = actorId, Action = "Paid", TimestampUtc = timestampUtc, PreviousStatus = ExpenseStatus.Approved, NewStatus = ExpenseStatus.Paid });
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPaymentUniquenessViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PaymentAlreadyRecordedException();
        }

        await transaction.CommitAsync(cancellationToken);
        return expense;
    }

    private static bool IsPaymentUniquenessViolation(DbUpdateException exception)
    {
        string message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("IX_PaymentRecords_ExpenseId", StringComparison.OrdinalIgnoreCase) ||
            (message.Contains("PaymentRecords", StringComparison.OrdinalIgnoreCase) && message.Contains("ExpenseId", StringComparison.OrdinalIgnoreCase));
    }

    public Task<List<ExpenseHistory>> FindHistoryAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken) => _dbContext.ExpenseHistories
        .AsNoTracking()
        .Where(history => history.ExpenseId == id)
        .OrderBy(history => history.TimestampUtc)
        .ThenBy(history => history.Id)
        .ToListAsync(cancellationToken);

    public Task<List<Expense>> GetVisibleAsync(ClaimsPrincipal user, CancellationToken cancellationToken) => _dbContext.Expenses
        .AsNoTracking()
        .Include(expense => expense.Category)
        .Where(ExpenseVisibility.Build(user))
        .ToListAsync(cancellationToken);

    public Task<Expense?> FindVisibleAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken) => _dbContext.Expenses
        .AsNoTracking()
        .Include(expense => expense.Category)
        .Where(ExpenseVisibility.Build(user))
        .SingleOrDefaultAsync(expense => expense.Id == id, cancellationToken);

    public Task AddAsync(Expense expense, CancellationToken cancellationToken) => _dbContext.Expenses.AddAsync(expense, cancellationToken).AsTask();

    public Task SaveAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
