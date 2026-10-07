namespace ExpenseHub.Api.Security;

internal static class AuthorizationPolicies
{
    public const string Admin = "RequireAdmin";
    public const string Employee = "RequireEmployee";
    public const string Approver = "RequireApprover";
    public const string Finance = "RequireFinance";
    public const string Auditor = "RequireAuditor";
}
