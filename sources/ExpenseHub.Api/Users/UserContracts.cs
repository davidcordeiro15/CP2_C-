using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExpenseHub.Api.Users;

internal sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

internal sealed record RegisterResponse(string Id, string Email);

internal sealed class UpdateRolesRequest
{
    public string[]? Roles { get; set; }
}

internal sealed record UserSummary(string Id, string Email, IReadOnlyList<string> Roles);

internal sealed record ServiceError(string Code, string Message);

internal sealed record ServiceResult<T>(T? Value, ServiceError? Error)
{
    public bool Succeeded => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, null);

    public static ServiceResult<T> Failure(string code, string message) => new(default, new ServiceError(code, message));
}
