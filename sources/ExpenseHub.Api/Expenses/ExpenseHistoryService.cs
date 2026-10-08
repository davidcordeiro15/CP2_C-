using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Expenses;

internal sealed class ExpenseHistoryService : IExpenseHistoryService
{
    private readonly IExpenseDraftRepository _repository;
    private readonly IExpenseQueryRepository _queryRepository;

    public ExpenseHistoryService(IExpenseDraftRepository repository, IExpenseQueryRepository queryRepository)
    {
        _repository = repository;
        _queryRepository = queryRepository;
    }

    public async Task<DraftServiceResult<IReadOnlyList<ExpenseHistoryResponse>>> GetAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DraftServiceResult<IReadOnlyList<ExpenseHistoryResponse>>.Failure("Unauthorized", "The authenticated user identifier is missing.");
        }

        if (!ExpenseVisibility.HasReadScope(user))
        {
            return DraftServiceResult<IReadOnlyList<ExpenseHistoryResponse>>.Failure("Forbidden", "The authenticated user does not have a functional expense role.");
        }

        Expense? expense = await _queryRepository.FindVisibleAsync(id, user, cancellationToken);
        if (expense is null)
        {
            return DraftServiceResult<IReadOnlyList<ExpenseHistoryResponse>>.Failure("NotFound", "Expense was not found.");
        }

        List<ExpenseHistory> histories = await _repository.FindHistoryAsync(id, user, cancellationToken);
        IReadOnlyList<ExpenseHistoryResponse> response = histories
            .OrderBy(history => history.TimestampUtc)
            .ThenBy(history => history.Id)
            .Select(history => new ExpenseHistoryResponse(
                history.Id,
                history.ExpenseId,
                history.Action,
                history.ActorId,
                history.TimestampUtc,
                history.PreviousStatus,
                history.NewStatus,
                history.RejectionReason,
                history.DraftChanges))
            .ToList();

        return DraftServiceResult<IReadOnlyList<ExpenseHistoryResponse>>.Success(response);
    }
}
