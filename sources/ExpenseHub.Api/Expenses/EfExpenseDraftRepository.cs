using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExpenseHub.Api.Expenses;

internal sealed class EfExpenseDraftRepository : IExpenseDraftRepository
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

    public Task<ExpenseCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken) => _dbContext.ExpenseCategories
        .SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

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
