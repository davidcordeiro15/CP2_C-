using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal interface IExpenseDraftRepository
{
    Task<Expense?> FindOwnedAsync(int id, string ownerId, CancellationToken cancellationToken);
    Task<Expense?> FindOwnedReadOnlyAsync(int id, string ownerId, CancellationToken cancellationToken);
    Task<Expense?> FindByIdReadOnlyAsync(int id, CancellationToken cancellationToken);
    Task<Expense?> SubmitOwnedAsync(int id, string ownerId, DateTime submittedAtUtc, CancellationToken cancellationToken);
    Task<Expense?> UpdateOwnedDraftAsync(int id, string ownerId, ExpenseDraftRequest request, List<DraftChange> changes, DateTime timestampUtc, CancellationToken cancellationToken);
    Task<Expense?> DecideAsync(int id, string actorId, ExpenseStatus targetStatus, string? justification, DateTime timestampUtc, CancellationToken cancellationToken);
    Task<Expense?> PayAsync(int id, string actorId, DateTime timestampUtc, CancellationToken cancellationToken);
    Task<List<ExpenseHistory>> FindHistoryAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<ExpenseCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken);
    Task AddAsync(Expense expense, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task BeginAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

internal interface IExpenseDraftService
{
    Task<DraftServiceResult<ExpenseDraftResponse>> CreateAsync(ExpenseDraftRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<DraftServiceResult<ExpenseDraftResponse>> UpdateAsync(int id, ExpenseDraftRequest request, ClaimsPrincipal user, CancellationToken cancellationToken);
}

internal interface IExpenseQueryRepository
{
    Task<Expense?> FindByIdAsync(int id, CancellationToken cancellationToken);
    Task<Expense?> FindByIdReadOnlyAsync(int id, CancellationToken cancellationToken);
    Task<List<Expense>> GetVisibleAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<Expense?> FindVisibleAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
}

internal interface IExpenseSubmitService
{
    Task<DraftServiceResult<ExpenseDraftResponse>> SubmitAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
}

internal interface IExpenseDecisionService
{
    Task<DraftServiceResult<ExpenseDraftResponse>> DecideAsync(int id, ClaimsPrincipal user, ExpenseStatus targetStatus, string? justification, CancellationToken cancellationToken);
}

internal interface IExpensePaymentService
{
    Task<DraftServiceResult<ExpenseDraftResponse>> PayAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
}

internal interface IExpenseHistoryService
{
    Task<DraftServiceResult<IReadOnlyList<ExpenseHistoryResponse>>> GetAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
}

internal interface IExpenseQueryService
{
    Task<DraftServiceResult<ExpenseDraftResponse>> GetAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseDraftResponse>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
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

    public async Task<DraftServiceResult<ExpenseDraftResponse>> CreateAsync(ExpenseDraftRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        DraftServiceResult<string> authorization = AuthorizeEmployee(user, "Only employees can create drafts.");
        if (!authorization.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(authorization.ErrorCode!, authorization.ErrorMessage!);
        }

        string ownerId = authorization.Value!;
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

    public async Task<DraftServiceResult<ExpenseDraftResponse>> UpdateAsync(int id, ExpenseDraftRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        DraftServiceResult<string> authorization = AuthorizeEmployee(user, "Only employees can edit drafts.");
        if (!authorization.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(authorization.ErrorCode!, authorization.ErrorMessage!);
        }

        string ownerId = authorization.Value!;
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

        DateTime timestampUtc = _timeProvider.GetUtcNow().UtcDateTime;
        Expense? updatedExpense = await _repository.UpdateOwnedDraftAsync(id, ownerId, request, changes, timestampUtc, cancellationToken);

        if (updatedExpense is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Draft expenses can be edited.");
        }

        return DraftServiceResult<ExpenseDraftResponse>.Success(ToResponse(updatedExpense, categoryResult.Value!));
    }

    private static DraftServiceResult<string> AuthorizeEmployee(ClaimsPrincipal user, string message)
    {
        if (!user.IsInRole(ExpenseHubRoles.Employee))
        {
            return DraftServiceResult<string>.Failure("Forbidden", message);
        }

        string? ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(ownerId)
            ? DraftServiceResult<string>.Failure("Unauthorized", "The authenticated user identifier is missing.")
            : DraftServiceResult<string>.Success(ownerId);
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
