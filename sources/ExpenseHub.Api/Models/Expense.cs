using System;
using System.Collections.Generic;
namespace ExpenseHub.Api.Models;

internal sealed class Expense
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public ApplicationUser? Owner { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime ExpenseDate { get; set; }

    public int CategoryId { get; set; }

    public ExpenseCategory? Category { get; set; }

    public ExpenseStatus Status { get; set; } = ExpenseStatus.Draft;

    public ICollection<ExpenseHistory> Histories { get; set; } = new List<ExpenseHistory>();

    public PaymentRecord? PaymentRecord { get; set; }
}
