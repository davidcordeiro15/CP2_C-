using System;

namespace ExpenseHub.UnitTests;

internal static class SyntheticValues
{
    public static string CreateEmail() => $"user-{Guid.NewGuid():N}@example.com";
    public static string CreatePassword() => $"Password-{Guid.NewGuid():N}-Aa1!";
    public static string CreateInvalidPassword() => $"short-{Guid.NewGuid():N}";
}
