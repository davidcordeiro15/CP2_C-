namespace ExpenseHub.Api.Security;

internal static class ExpenseHubRoles
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";
    public const string Approver = "Approver";
    public const string Finance = "Finance";
    public const string Auditor = "Auditor";

    public static readonly string[] All =
    [
        Admin,
        Employee,
        Approver,
        Finance,
        Auditor
    ];
}
