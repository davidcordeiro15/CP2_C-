using System;
using System.Linq.Expressions;
using System.Security.Claims;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;

namespace ExpenseHub.Api.Expenses;

internal static class ExpenseVisibility
{
    public static Expression<Func<Expense, bool>> Build(ClaimsPrincipal user)
    {
        bool seesAll = user.IsInRole(ExpenseHubRoles.Auditor);
        bool seesOwn = user.IsInRole(ExpenseHubRoles.Employee);
        bool seesSubmitted = user.IsInRole(ExpenseHubRoles.Approver);
        bool seesApprovedOrPaid = user.IsInRole(ExpenseHubRoles.Finance);
        string ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? string.Empty;

        return expense =>
            seesAll ||
            (seesOwn && expense.OwnerId == ownerId) ||
            (seesSubmitted && expense.Status == ExpenseStatus.Submitted) ||
            (seesApprovedOrPaid && (expense.Status == ExpenseStatus.Approved || expense.Status == ExpenseStatus.Paid));
    }

    public static bool HasReadScope(ClaimsPrincipal user)
    {
        return user.IsInRole(ExpenseHubRoles.Auditor) ||
            user.IsInRole(ExpenseHubRoles.Employee) ||
            user.IsInRole(ExpenseHubRoles.Approver) ||
            user.IsInRole(ExpenseHubRoles.Finance);
    }
}
