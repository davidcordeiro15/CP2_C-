using System;
using System.ComponentModel.DataAnnotations;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Expenses;

internal sealed class ExpenseDraftRequest
{
    [Required]
    [MinLength(10)]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 2147483647d)]
    public decimal Amount { get; set; }

    [Required]
    public DateTime? ExpenseDate { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
}

internal sealed record ExpenseDraftResponse(int Id, string OwnerId, string Description, decimal Amount, DateTime ExpenseDate, int CategoryId, string CategoryName, ExpenseStatus Status);

internal sealed class ExpenseRejectRequest
{
    [Required]
    [MinLength(10)]
    [MaxLength(500)]
    public string Justification { get; set; } = string.Empty;
}

internal sealed record DraftServiceResult<T>(T? Value, string? ErrorCode, string? ErrorMessage)
{
    public bool Succeeded => ErrorCode is null;
    public static DraftServiceResult<T> Success(T value) => new(value, null, null);
    public static DraftServiceResult<T> Failure(string code, string message) => new(default, code, message);
}

internal sealed record DraftChange(string Field, string PreviousValue, string NewValue);
