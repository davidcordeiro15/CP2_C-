using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Expenses;

internal sealed class ExpenseQueryService : IExpenseQueryService
{
    private readonly IExpenseQueryRepository _repository;

    public ExpenseQueryService(IExpenseQueryRepository repository)
    {
        _repository = repository;
    }

    public async Task<DraftServiceResult<ExpenseDraftResponse>> GetAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        Expense? expense = await _repository.FindVisibleAsync(id, user, cancellationToken);
        return expense is null
            ? DraftServiceResult<ExpenseDraftResponse>.Failure("NotFound", "Expense was not found.")
            : DraftServiceResult<ExpenseDraftResponse>.Success(ToResponse(expense));
    }

    public async Task<IReadOnlyList<ExpenseDraftResponse>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        List<Expense> expenses = await _repository.GetVisibleAsync(user, cancellationToken);
        return expenses.Select(ToResponse).ToList();
    }

    private static ExpenseDraftResponse ToResponse(Expense expense)
    {
        return new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId, expense.Category?.Name ?? string.Empty, expense.Status);
    }
}

internal sealed class ExpenseSubmitService : IExpenseSubmitService
{
    private readonly IExpenseQueryRepository _queryRepository;
    private readonly IExpenseDraftRepository _draftRepository;
    private readonly TimeProvider _timeProvider;

    public ExpenseSubmitService(IExpenseQueryRepository queryRepository, IExpenseDraftRepository draftRepository, TimeProvider timeProvider)
    {
        _queryRepository = queryRepository;
        _draftRepository = draftRepository;
        _timeProvider = timeProvider;
    }

    public async Task<DraftServiceResult<ExpenseDraftResponse>> SubmitAsync(int id, string ownerId, CancellationToken cancellationToken)
    {
        Expense? existing = await _draftRepository.FindOwnedReadOnlyAsync(id, ownerId, cancellationToken);
        if (existing is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("NotFound", "Expense was not found.");
        }

        if (existing.Status != ExpenseStatus.Draft)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Draft expenses can be submitted.");
        }

        Expense? expense = await _draftRepository.SubmitOwnedAsync(id, ownerId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (expense is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Draft expenses can be submitted.");
        }

        return DraftServiceResult<ExpenseDraftResponse>.Success(new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId, expense.Category?.Name ?? string.Empty, expense.Status));
    }
}
