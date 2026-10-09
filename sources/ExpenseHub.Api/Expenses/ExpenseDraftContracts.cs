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
    [DecimalPrecision(2)]
    public decimal Amount { get; set; }

    [Required]
    public DateTime? ExpenseDate { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class DecimalPrecisionAttribute : ValidationAttribute
{
    private readonly int _scale;

    public DecimalPrecisionAttribute(int scale)
    {
        _scale = scale;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is decimal d && decimal.Round(d, _scale) != d)
        {
            return new ValidationResult($"The amount must have at most {_scale} decimal places.");
        }

        return ValidationResult.Success;
    }
}

internal sealed record ExpenseDraftResponse(int Id, string OwnerId, string Description, decimal Amount, DateTime ExpenseDate, int CategoryId, string CategoryName, ExpenseStatus Status);

internal sealed record ExpenseHistoryResponse(int Id, int ExpenseId, string Action, string ActorId, DateTime TimestampUtc, ExpenseStatus? PreviousStatus, ExpenseStatus? NewStatus, string? RejectionReason, string? DraftChanges);

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
