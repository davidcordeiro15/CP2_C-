using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Testes de envio e visibilidade.</summary>
[TestClass]
public sealed class ExpenseQueryAndSubmitTests
{
    /// <summary>Valida envio próprio e repetição.</summary>
    [TestMethod]
    public async Task SubmitOwnDraftCreatesOneHistoryAndRepeatedReturnsConflict()
    {
        InMemoryQueryRepository repository = CreateRepository();
        ExpenseSubmitService service = new(repository, repository, new FixedTimeProvider(DateTimeOffset.UtcNow));
        DraftServiceResult<ExpenseDraftResponse> first = await service.SubmitAsync(1, Principal("owner", ExpenseHubRoles.Employee), CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> repeated = await service.SubmitAsync(1, Principal("owner", ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(repeated.Succeeded);
        Assert.AreEqual("Conflict", repeated.ErrorCode);
        Assert.HasCount(1, repository.Expenses[0].Histories);
    }

    /// <summary>Valida 404 para outro proprietário e inexistente.</summary>
    [TestMethod]
    public async Task SubmitOtherOwnerOrMissingReturnsNotFound()
    {
        InMemoryQueryRepository repository = CreateRepository();
        ExpenseSubmitService service = new(repository, repository, TimeProvider.System);
        DraftServiceResult<ExpenseDraftResponse> other = await service.SubmitAsync(1, Principal("other", ExpenseHubRoles.Employee), CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> missing = await service.SubmitAsync(999, Principal("owner", ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.AreEqual("NotFound", other.ErrorCode);
        Assert.AreEqual("NotFound", missing.ErrorCode);
    }

    /// <summary>Valida escopos de Employee, Approver, Finance e Auditor.</summary>
    [TestMethod]
    public async Task VisibilityScopesApplyRolesAndUnions()
    {
        InMemoryQueryRepository repository = CreateRepository();
        repository.Expenses.Add(CreateExpense(2, "other", ExpenseStatus.Submitted));
        repository.Expenses.Add(CreateExpense(3, "other", ExpenseStatus.Approved));
        ExpenseQueryService service = new(repository);
        Assert.HasCount(1, await service.ListAsync(Principal("owner", ExpenseHubRoles.Employee), CancellationToken.None));
        Assert.HasCount(1, await service.ListAsync(Principal("a", ExpenseHubRoles.Approver), CancellationToken.None));
        Assert.HasCount(1, await service.ListAsync(Principal("f", ExpenseHubRoles.Finance), CancellationToken.None));
        Assert.HasCount(3, await service.ListAsync(Principal("x", ExpenseHubRoles.Auditor), CancellationToken.None));
        Assert.HasCount(2, await service.ListAsync(Principal("owner", ExpenseHubRoles.Employee, ExpenseHubRoles.Approver), CancellationToken.None));
    }

    /// <summary>Valida Admin isolado e detalhe invisível.</summary>
    [TestMethod]
    public async Task AdminOnlyHasNoScopeAndInvisibleDetailIsNotFound()
    {
        InMemoryQueryRepository repository = CreateRepository();
        ExpenseQueryService service = new(repository);
        await Assert.ThrowsExactlyAsync<ExpenseAuthorizationException>(() => service.ListAsync(Principal("admin", ExpenseHubRoles.Admin), CancellationToken.None));
        DraftServiceResult<ExpenseDraftResponse> result = await service.GetAsync(1, Principal("other", ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.AreEqual("NotFound", result.ErrorCode);
    }

    /// <summary>Rejeita leitura sem identidade funcional.</summary>
    [TestMethod]
    public async Task QueryServicesRejectMissingIdentityAndFunctionalRole()
    {
        InMemoryQueryRepository repository = CreateRepository();
        ExpenseQueryService service = new(repository);
        DraftServiceResult<ExpenseDraftResponse> missingIdentity = await service.GetAsync(1, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> noRole = await service.GetAsync(1, Principal("admin", ExpenseHubRoles.Admin), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ExpenseAuthorizationException>(() => service.ListAsync(Principal("admin", ExpenseHubRoles.Admin), CancellationToken.None));
        Assert.AreEqual("Unauthorized", missingIdentity.ErrorCode);
        Assert.AreEqual("Forbidden", noRole.ErrorCode);
    }

    /// <summary>Auditor isolado não envia despesa e Employee acumulado envia a própria.</summary>
    [TestMethod]
    public async Task AuditorEmployeeRetainsOwnSubmitPermissionButAuditorOnlyDoesNot()
    {
        InMemoryQueryRepository repository = CreateRepository();
        ExpenseSubmitService service = new(repository, repository, TimeProvider.System);
        DraftServiceResult<ExpenseDraftResponse> auditor = await service.SubmitAsync(1, Principal("owner", ExpenseHubRoles.Auditor), CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> employeeAuditor = await service.SubmitAsync(1, Principal("owner", ExpenseHubRoles.Auditor, ExpenseHubRoles.Employee), CancellationToken.None);
        Assert.AreEqual("Forbidden", auditor.ErrorCode);
        Assert.IsTrue(employeeAuditor.Succeeded);
    }

    private static ClaimsPrincipal Principal(string id, params string[] roles) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), .. roles.Select(role => new Claim(ClaimTypes.Role, role))], "test"));
    private static Expense CreateExpense(int id, string owner, ExpenseStatus status) => new() { Id = id, OwnerId = owner, Description = "Valid expense description", Amount = 1, ExpenseDate = DateTime.UtcNow.Date, CategoryId = 1, Category = new ExpenseCategory { Id = 1, Name = "Meals" }, Status = status };
    private static InMemoryQueryRepository CreateRepository() => new([CreateExpense(1, "owner", ExpenseStatus.Draft)]);
}

internal sealed class InMemoryQueryRepository : IExpenseDraftRepository, IExpenseQueryRepository
{
    public InMemoryQueryRepository(List<Expense> expenses) { Expenses = expenses; }
    public List<Expense> Expenses { get; }
    public Exception? PaymentException { get; set; }
    public Task<Expense?> FindOwnedAsync(int id, string ownerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Expenses.SingleOrDefault(item => item.Id == id && item.OwnerId == ownerId));
    }

    public Task<Expense?> FindOwnedReadOnlyAsync(int id, string ownerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Expenses.SingleOrDefault(item => item.Id == id && item.OwnerId == ownerId));
    }

    public Task<Expense?> SubmitOwnedAsync(int id, string ownerId, DateTime timestamp, CancellationToken cancellationToken)
    {
        Expense? expense = Expenses.SingleOrDefault(item => item.Id == id && item.OwnerId == ownerId && item.Status == ExpenseStatus.Draft);
        if (expense is null)
        {
            return Task.FromResult<Expense?>(null);
        }

        expense.Status = ExpenseStatus.Submitted;
        expense.Histories.Add(new ExpenseHistory
        {
            Action = "Submitted",
            ActorId = ownerId,
            TimestampUtc = timestamp,
            PreviousStatus = ExpenseStatus.Draft,
            NewStatus = ExpenseStatus.Submitted
        });

        return Task.FromResult<Expense?>(expense);
    }
    public Task<Expense?> FindByIdReadOnlyAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Expenses.SingleOrDefault(item => item.Id == id));
    public Task<Expense?> DecideAsync(int id, string actorId, ExpenseStatus targetStatus, string? justification, DateTime timestamp, CancellationToken cancellationToken)
    {
        Expense? expense = Expenses.SingleOrDefault(item => item.Id == id && item.Status == ExpenseStatus.Submitted);
        if (expense is null)
        {
            return Task.FromResult<Expense?>(null);
        }

        ExpenseStatus previous = expense.Status;
        expense.Status = targetStatus;
        expense.Histories.Add(new ExpenseHistory
        {
            Action = targetStatus == ExpenseStatus.Approved ? "Approved" : "Rejected",
            ActorId = actorId,
            TimestampUtc = timestamp,
            PreviousStatus = previous,
            NewStatus = targetStatus,
            RejectionReason = justification
        });
        return Task.FromResult<Expense?>(expense);
    }
    public Task<Expense?> PayAsync(int id, string actorId, DateTime timestamp, CancellationToken cancellationToken)
    {
        if (PaymentException is not null)
        {
            throw PaymentException;
        }

        Expense? expense = Expenses.SingleOrDefault(item => item.Id == id && item.Status == ExpenseStatus.Approved);
        if (expense is null)
        {
            return Task.FromResult<Expense?>(null);
        }

        expense.Status = ExpenseStatus.Paid;
        expense.PaymentRecord = new PaymentRecord { ExpenseId = id, PayerId = actorId, PaidAtUtc = timestamp };
        expense.Histories.Add(new ExpenseHistory { Action = "Paid", ActorId = actorId, TimestampUtc = timestamp, PreviousStatus = ExpenseStatus.Approved, NewStatus = ExpenseStatus.Paid });
        return Task.FromResult<Expense?>(expense);
    }
    public Task<List<ExpenseHistory>> FindHistoryAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken) => Task.FromResult(Expenses.SingleOrDefault(item => item.Id == id)?.Histories.OrderBy(history => history.TimestampUtc).ThenBy(history => history.Id).ToList() ?? []);
    public Task<ExpenseCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken) => Task.FromResult<ExpenseCategory?>(null);
    public Task AddAsync(Expense expense, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<Expense?> FindByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Expenses.SingleOrDefault(item => item.Id == id));
    public Task<List<Expense>> GetVisibleAsync(ClaimsPrincipal user, CancellationToken cancellationToken) => Task.FromResult(Expenses.Where(ExpenseVisibility.Build(user).Compile()).ToList());
    public Task<Expense?> FindVisibleAsync(int id, ClaimsPrincipal user, CancellationToken cancellationToken) => Task.FromResult(Expenses.Where(ExpenseVisibility.Build(user).Compile()).SingleOrDefault(item => item.Id == id));
}
