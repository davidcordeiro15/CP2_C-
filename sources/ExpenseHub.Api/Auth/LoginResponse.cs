namespace ExpenseHub.Api.Auth;

internal sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);
