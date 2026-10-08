using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal sealed class ExpenseDecisionService : IExpenseDecisionService
{
    private readonly IExpenseDraftRepository _draftRepository;
    private readonly IExpenseQueryRepository _queryRepository;
    private readonly TimeProvider _timeProvider;

    public ExpenseDecisionService(IExpenseDraftRepository draftRepository, IExpenseQueryRepository queryRepository, TimeProvider timeProvider)
    {
        _draftRepository = draftRepository;
        _queryRepository = queryRepository;
        _timeProvider = timeProvider;
    }

    public async Task<DraftServiceResult<ExpenseDraftResponse>> DecideAsync(int id, ClaimsPrincipal user, ExpenseStatus targetStatus, string? justification, CancellationToken cancellationToken)
    {
        DraftServiceResult<object> auth = AuthorizeDecision(user);
        if (!auth.Succeeded)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure(auth.ErrorCode!, auth.ErrorMessage!);
        }

        string actorId = auth.Value!.ToString()!;

        Expense? existing = await _queryRepository.FindByIdReadOnlyAsync(id, cancellationToken);
        if (existing is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("NotFound", "Expense was not found.");
        }

        if (string.Equals(actorId, existing.OwnerId, StringComparison.Ordinal))
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Forbidden", "An expense cannot be decided by its owner.");
        }

        if (existing.Status != ExpenseStatus.Submitted)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Submitted expenses can be decided.");
        }

        string? normalizedJustification = null;
        if (targetStatus == ExpenseStatus.Rejected)
        {
            DraftServiceResult<object> validation = ValidateJustification(justification);
            if (!validation.Succeeded)
            {
                return DraftServiceResult<ExpenseDraftResponse>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
            }

            normalizedJustification = validation.Value!.ToString();
        }

        Expense? expense = await _draftRepository.DecideAsync(id, actorId, targetStatus, normalizedJustification, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (expense is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Submitted expenses can be decided.");
        }

        return DraftServiceResult<ExpenseDraftResponse>.Success(new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId, expense.Category?.Name ?? string.Empty, expense.Status));
    }

    private static DraftServiceResult<object> AuthorizeDecision(ClaimsPrincipal user)
    {
        string? actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return DraftServiceResult<object>.Failure("Unauthorized", "The authenticated user identifier is missing.");
        }

        if (!user.IsInRole(ExpenseHubRoles.Approver))
        {
            return DraftServiceResult<object>.Failure("Forbidden", "Only approvers can decide on an expense.");
        }

        return DraftServiceResult<object>.Success(actorId);
    }

    private static DraftServiceResult<object> ValidateJustification(string? justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
        {
            return DraftServiceResult<object>.Failure("InvalidJustification", "Justification is required.");
        }

        string normalized = justification.Trim();
        if (normalized.Length < 10)
        {
            return DraftServiceResult<object>.Failure("InvalidJustification", "Justification must be at least 10 characters.");
        }

        if (normalized.Length > 500)
        {
            return DraftServiceResult<object>.Failure("InvalidJustification", "Justification must not exceed 500 characters.");
        }

        return DraftServiceResult<object>.Success(normalized);
    }
}