using System;
namespace ExpenseHub.Api.Models;

internal sealed class ExpenseHistory
{
    public int Id { get; set; }

    public int ExpenseId { get; set; }

    public Expense? Expense { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public ApplicationUser? Actor { get; set; }

    public DateTime TimestampUtc { get; set; }

    public ExpenseStatus? PreviousStatus { get; set; }

    public ExpenseStatus? NewStatus { get; set; }

    public string? RejectionReason { get; set; }

    public string? DraftChanges { get; set; }
}
