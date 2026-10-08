using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal sealed class ExpensePaymentService : IExpensePaymentService
{
    private readonly IExpenseDraftRepository _repository;
    private readonly IExpenseQueryRepository _queryRepository;
    private readonly TimeProvider _timeProvider;

    public ExpensePaymentService(IExpenseDraftRepository repository, IExpenseQueryRepository queryRepository, TimeProvider timeProvider)
    {
        _repository = repository;
        _queryRepository = queryRepository;
        _timeProvider = timeProvider;
    }

    public async Task<DraftServiceResult<ExpenseDraftResponse>> PayAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        DraftServiceResult<object> auth = AuthorizePayment(user);
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
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Forbidden", "An expense cannot be paid by its owner.");
        }

        if (existing.Status != ExpenseStatus.Approved)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Approved expenses can be paid.");
        }

        Expense? expense;
        try
        {
            expense = await _repository.PayAsync(id, actorId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch (PaymentAlreadyRecordedException)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Approved expenses can be paid.");
        }

        if (expense is null)
        {
            return DraftServiceResult<ExpenseDraftResponse>.Failure("Conflict", "Only Approved expenses can be paid.");
        }

        return DraftServiceResult<ExpenseDraftResponse>.Success(new(expense.Id, expense.OwnerId, expense.Description, expense.Amount, expense.ExpenseDate, expense.CategoryId, expense.Category?.Name ?? string.Empty, expense.Status));
    }

    private static DraftServiceResult<object> AuthorizePayment(ClaimsPrincipal user)
    {
        string? actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return DraftServiceResult<object>.Failure("Unauthorized", "The authenticated user identifier is missing.");
        }

        if (!user.IsInRole(ExpenseHubRoles.Finance))
        {
            return DraftServiceResult<object>.Failure("Forbidden", "Only finance users can register a payment.");
        }

        return DraftServiceResult<object>.Success(actorId);
    }
}

internal sealed class PaymentAlreadyRecordedException : Exception
{
}
