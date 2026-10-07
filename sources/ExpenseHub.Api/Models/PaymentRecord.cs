using System;
namespace ExpenseHub.Api.Models;

internal sealed class PaymentRecord
{
    public int Id { get; set; }

    public int ExpenseId { get; set; }

    public Expense? Expense { get; set; }

    public string PayerId { get; set; } = string.Empty;

    public ApplicationUser? Payer { get; set; }

    public DateTime PaidAtUtc { get; set; }
}
