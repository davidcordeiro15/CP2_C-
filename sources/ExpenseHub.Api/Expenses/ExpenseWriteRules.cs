using System;
using System.Security.Claims;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal static class ExpenseWriteRules
{
    public static DraftServiceResult<object> AuthorizeDecision(ClaimsPrincipal user, Expense expense)
    {
        DraftServiceResult<object> actor = ResolveActor(user);
        if (!actor.Succeeded)
        {
            return actor;
        }

        if (!user.IsInRole(ExpenseHubRoles.Approver))
        {
            return DraftServiceResult<object>.Failure("Forbidden", "Only approvers can decide on an expense.");
        }

        if (string.Equals(actor.Value?.ToString(), expense.OwnerId, StringComparison.Ordinal))
        {
            return DraftServiceResult<object>.Failure("Forbidden", "An expense cannot be decided by its owner.");
        }

        return expense.Status == ExpenseStatus.Submitted ? DraftServiceResult<object>.Success(new object()) : DraftServiceResult<object>.Failure("Conflict", "Only Submitted expenses can be decided.");
    }

    public static DraftServiceResult<object> AuthorizePayment(ClaimsPrincipal user, Expense expense)
    {
        DraftServiceResult<object> actor = ResolveActor(user);
        if (!actor.Succeeded)
        {
            return actor;
        }

        if (!user.IsInRole(ExpenseHubRoles.Finance))
        {
            return DraftServiceResult<object>.Failure("Forbidden", "Only finance users can register a payment.");
        }

        if (string.Equals(actor.Value?.ToString(), expense.OwnerId, StringComparison.Ordinal))
        {
            return DraftServiceResult<object>.Failure("Forbidden", "An expense cannot be paid by its owner.");
        }

        return expense.Status == ExpenseStatus.Approved ? DraftServiceResult<object>.Success(new object()) : DraftServiceResult<object>.Failure("Conflict", "Only Approved expenses can be paid.");
    }

    private static DraftServiceResult<object> ResolveActor(ClaimsPrincipal user)
    {
        string? actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(actorId) ? DraftServiceResult<object>.Failure("Unauthorized", "The authenticated user identifier is missing.") : DraftServiceResult<object>.Success(actorId);
    }
}
