using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Expenses;

internal interface IExpenseDraftRepository
{
    Task<Expense?> FindOwnedAsync(int id, string ownerId, CancellationToken cancellationToken);
    Task<ExpenseCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken);
    Task AddAsync(Expense expense, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task BeginAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

internal interface IExpenseDraftService
{
    Task<DraftServiceResult<ExpenseDraftResponse>> CreateAsync(ExpenseDraftRequest request, string ownerId, CancellationToken cancellationToken);
    Task<DraftServiceResult<ExpenseDraftResponse>> UpdateAsync(int id, ExpenseDraftRequest request, string ownerId, CancellationToken cancellationToken);
}

internal sealed class ExpenseDraftService : IExpenseDraftService
{
    private readonly IExpenseDraftRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ExpenseDraftService(IExpenseDraftRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<DraftServiceResult<ExpenseDraftResponse>> CreateAsync(ExpenseDraftRequest request, string ownerId, CancellationToken cancellationToken)
    {
        DraftServiceResult<ExpenseCategory> categoryResult = await FindCategoryAsync(request.CategoryId, cancellationToken);
        if (!categoryResult.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(categoryResult.ErrorCode!, categoryResult.ErrorMessage!);
        }

        DraftServiceResult<object> validation = ValidateDate(request.ExpenseDate!.Value);
        if (!validation.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        Expense expense = new()
        {
            OwnerId = ownerId,
            Description = request.Description,
            Amount = request.Amount,
            ExpenseDate = request.ExpenseDate.Value.Date,
            CategoryId = request.CategoryId,
            Category = categoryResult.Value,
            Status = ExpenseStatus.Draft
        };
        expense.Histories.Add(CreateHistory(expense, ownerId, "Created", null, SerializeChanges(request, null)));

        await _repository.BeginAsync(cancellationToken);
        try
        {
            await _repository.AddAsync(expense, cancellationToken);
            await _repository.SaveAsync(cancellationToken);
            await _repository.CommitAsync(cancellationToken);
        }
        catch
        {
            await _repository.RollbackAsync(cancellationToken);
            throw;
        }

        return DraftServiceResult<ExpenseDraftResponse>.Success(ToResponse(expense, categoryResult.Value!));
    }

    public async Task<DraftServiceResult<ExpenseDraftResponse>> UpdateAsync(int id, ExpenseDraftRequest request, string ownerId, CancellationToken cancellationToken)
    {
        Expense? expense = await _repository.FindOwnedAsync(id, ownerId, cancellationToken);
        if (expense is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("NotFound", "Expense was not found.");
        }

        if (expense.Status != ExpenseStatus.Draft)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Draft expenses can be edited.");
        }

        DraftServiceResult<ExpenseCategory> categoryResult = await FindCategoryAsync(request.CategoryId, cancellationToken);
        if (!categoryResult.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(categoryResult.ErrorCode!, categoryResult.ErrorMessage!);
        }

        DraftServiceResult<object> validation = ValidateDate(request.ExpenseDate!.Value);
        if (!validation.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        List<DraftChange> changes = GetChanges(expense, request);
        if (changes.Count == 0)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Success(ToResponse(expense, categoryResult.Value!));
        }

        expense.Description = request.Description;
        expense.Amount = request.Amount;
        expense.ExpenseDate = request.ExpenseDate.Value.Date;
        expense.CategoryId = request.CategoryId;
        expense.Category = categoryResult.Value;
        expense.Histories.Add(CreateHistory(expense, ownerId, "Updated", ExpenseStatus.Draft, SerializeChanges(request, changes)));

        await _repository.BeginAsync(cancellationToken);
        try
        {
            await _repository.SaveAsync(cancellationToken);
            await _repository.CommitAsync(cancellationToken);
        }
        catch
        {
            await _repository.RollbackAsync(cancellationToken);
            throw;
        }

        return DraftServiceResult<ExpenseDraftResponse>.Success(ToResponse(expense, categoryResult.Value!));
    }

    private async Task<DraftServiceResult<ExpenseCategory>> FindCategoryAsync(int id, CancellationToken cancellationToken)
    {
        ExpenseCategory? category = await _repository.FindCategoryAsync(id, cancellationToken);
        return category is null
            ? DraftServiceResult<ExpenseCategory>.Failure("InvalidCategory", "The category does not exist.")
            : DraftServiceResult<ExpenseCategory>.Success(category);
    }

    private DraftServiceResult<object> ValidateDate(DateTime date)
    {
        DateTime today = _timeProvider.GetLocalNow().Date;
        return date.Date > today
            ? DraftServiceResult<object>.Failure("InvalidDate", "The expense date cannot be in the future.")
            : DraftServiceResult<object>.Success(new object());
    }

    private ExpenseHistory CreateHistory(Expense expense, string actorId, string action, ExpenseStatus? previousStatus, string? changes)
    {
        return new ExpenseHistory
        {
            Expense = expense,
            Action = action,
            ActorId = actorId,
            TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime,
            PreviousStatus = previousStatus,
            NewStatus = expense.Status,
            DraftChanges = changes
        };
    }

    private static List<DraftChange> GetChanges(Expense expense, ExpenseDraftRequest request)
    {
        List<DraftChange> changes = [];
        DateTime date = request.ExpenseDate!.Value.Date;
        if (!string.Equals(expense.Description, request.Description, StringComparison.Ordinal))
        {
            changes.Add(new("Description", expense.Description, request.Description));
        }

        if (expense.Amount != request.Amount)
        {
            changes.Add(new("Amount", expense.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (expense.ExpenseDate != date)
        {
            changes.Add(new("ExpenseDate", expense.ExpenseDate.ToString("O"), date.ToString("O")));
        }

        if (expense.CategoryId != request.CategoryId)
        {
            changes.Add(new("CategoryId", expense.CategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.CategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return changes;
    }

    private static string? SerializeChanges(ExpenseDraftRequest request, List<DraftChange>? changes)
    {
        return changes is null ? "Created draft" : string.Join(", ", changes.ConvertAll(change => $"{change.Field}: {change.PreviousValue} -> {change.NewValue}"));
    }

    private static ExpenseDraftResponse ToResponse(Expense expense, ExpenseCategory category)
    {
        return new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId, category.Name, expense.Status);
    }
}
