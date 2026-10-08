using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Testes de pagamento e histórico.</summary>
[TestClass]
public sealed class ExpensePaymentAndHistoryTests
{
    private static readonly DateTimeOffset _fixedNow = new(2032, 6, 7, 8, 9, 10, TimeSpan.Zero);

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task FinancePaysApprovedExpenseWithPaymentAndHistory()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved);
        ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
        DraftServiceResult<ExpenseDraftResponse> result = await service.PayAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None);
        Expense expense = repository.Expenses[0];
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ExpenseStatus.Paid, result.Value!.Status);
        Assert.AreEqual("finance", expense.PaymentRecord!.PayerId);
        Assert.AreEqual(_fixedNow.UtcDateTime, expense.PaymentRecord.PaidAtUtc);
        Assert.AreEqual(1, expense.Histories.Count(history => history.Action == "Paid"));
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task OwnerFinanceCannotPayOwnExpense()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved, "owner");
        ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
        DraftServiceResult<ExpenseDraftResponse> result = await service.PayAsync(1, Principal("owner", ExpenseHubRoles.Finance, ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.AreEqual("Forbidden", result.ErrorCode);
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task PayAsyncWithoutFinanceReturnsForbiddenAndDoesNotPersist()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved);
        ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
        DraftServiceResult<ExpenseDraftResponse> result = await service.PayAsync(1, Principal("employee", ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.AreEqual("Forbidden", result.ErrorCode);
        Assert.IsNull(repository.Expenses[0].PaymentRecord);
        Assert.AreEqual(0, repository.Expenses[0].Histories.Count(h => h.Action == "Paid"));
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task NonApprovedStatesReturnConflict()
    {
        foreach (ExpenseStatus status in new[] { ExpenseStatus.Draft, ExpenseStatus.Submitted, ExpenseStatus.Rejected, ExpenseStatus.Paid })
        {
            InMemoryQueryRepository repository = Repository(status);
            ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
            DraftServiceResult<ExpenseDraftResponse> result = await service.PayAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None);
            Assert.AreEqual("Conflict", result.ErrorCode);
        }
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task RepeatedPaymentReturnsConflictWithoutExtraRecords()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved);
        ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
        await service.PayAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None);
        PaymentRecord? paymentRecord = repository.Expenses[0].PaymentRecord;
        int paidHistoryCount = repository.Expenses[0].Histories.Count(h => h.Action == "Paid");
        DraftServiceResult<ExpenseDraftResponse> repeated = await service.PayAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None);
        Assert.AreEqual("Conflict", repeated.ErrorCode);
        Assert.AreSame(paymentRecord, repository.Expenses[0].PaymentRecord);
        Assert.AreEqual(paidHistoryCount, repository.Expenses[0].Histories.Count(h => h.Action == "Paid"));
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task HistoryUsesVisibilityAndReturnsDtos()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Paid);
        repository.Expenses[0].Histories.Add(new ExpenseHistory { Id = 2, ExpenseId = 1, Action = "Paid", ActorId = "finance", TimestampUtc = _fixedNow.UtcDateTime, PreviousStatus = ExpenseStatus.Approved, NewStatus = ExpenseStatus.Paid });
        ExpenseHistoryService service = new(repository, repository);
        DraftServiceResult<System.Collections.Generic.IReadOnlyList<ExpenseHistoryResponse>> result = await service.GetAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None);
        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(1, result.Value!);
        Assert.AreEqual("Paid", result.Value![0].Action);
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task PaymentAlreadyRecordedExceptionReturnsConflict()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved);
        repository.PaymentException = new PaymentAlreadyRecordedException();
        ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
        DraftServiceResult<ExpenseDraftResponse> result = await service.PayAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None);
        Assert.AreEqual("Conflict", result.ErrorCode);
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task UnrelatedDbUpdateExceptionIsNotMappedToConflict()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved);
        repository.PaymentException = new DbUpdateException();
        ExpensePaymentService service = new(repository, repository, new FixedTimeProvider(_fixedNow));
        DbUpdateException exception = await Assert.ThrowsExactlyAsync<DbUpdateException>(async () => await service.PayAsync(1, Principal("finance", ExpenseHubRoles.Finance), CancellationToken.None));
        Assert.IsNotNull(exception);
    }

    /// <summary>Valida comportamento esperado.</summary>

    [TestMethod]
    public async Task ApproverLosesHistoryVisibilityAfterApproval()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Approved);
        ExpenseHistoryService service = new(repository, repository);
        DraftServiceResult<System.Collections.Generic.IReadOnlyList<ExpenseHistoryResponse>> result = await service.GetAsync(1, Principal("approver", ExpenseHubRoles.Approver), CancellationToken.None);
        Assert.AreEqual("NotFound", result.ErrorCode);
    }

    /// <summary>Valida que Employee não consulta histórico de reembolso pago de outro usuário.</summary>
    [TestMethod]
    public async Task EmployeeCannotReadAnotherUsersPaidExpenseHistory()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Paid, owner: "other-user");
        ExpenseHistoryService service = new(repository, repository);
        DraftServiceResult<System.Collections.Generic.IReadOnlyList<ExpenseHistoryResponse>> result = await service.GetAsync(1, Principal("employee", ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.AreEqual("NotFound", result.ErrorCode);
    }

    private static InMemoryQueryRepository Repository(ExpenseStatus status, string owner = "owner") => new([new Expense { Id = 1, OwnerId = owner, Description = "Valid expense description", Amount = 1, ExpenseDate = _fixedNow.Date, CategoryId = 1, Category = new ExpenseCategory { Id = 1, Name = "Meals" }, Status = status }]);
    private static ClaimsPrincipal Principal(string id, params string[] roles) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), .. roles.Select(role => new Claim(ClaimTypes.Role, role))], "test"));
}
