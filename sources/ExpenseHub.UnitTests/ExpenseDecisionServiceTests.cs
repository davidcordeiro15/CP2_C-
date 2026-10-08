using System;
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

/// <summary>Testes de aprovação e reprovação de reembolsos enviados.</summary>
[TestClass]
public sealed class ExpenseDecisionServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2031, 4, 5, 10, 15, 0, TimeSpan.Zero);

    /// <summary>Aprovação transiciona para Approved com histórico do servidor.</summary>
    [TestMethod]
    public async Task ApproveSubmittedExpenseCreatesApprovedHistory()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Submitted);
        ExpenseDecisionService service = Service(repository);
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Approved, null, CancellationToken.None);
        ExpenseHistory history = repository.Expenses[0].Histories.Single();
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ExpenseStatus.Approved, result.Value!.Status);
        Assert.AreEqual("approver", history.ActorId);
        Assert.AreEqual(FixedNow.UtcDateTime, history.TimestampUtc);
        Assert.AreEqual(ExpenseStatus.Submitted, history.PreviousStatus);
        Assert.AreEqual(ExpenseStatus.Approved, history.NewStatus);
    }

    [TestMethod]
    public async Task RejectSubmittedExpensePersistsTrimmedJustification()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Submitted);
        ExpenseDecisionService service = Service(repository);
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Rejected, "  Justification is valid  ", CancellationToken.None);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ExpenseStatus.Rejected, result.Value!.Status);
        Assert.AreEqual("Justification is valid", repository.Expenses[0].Histories.Single().RejectionReason);
    }

    [TestMethod]
    public async Task MissingIdentityReturnsUnauthorized()
    {
        ExpenseDecisionService service = Service(Repository(ExpenseStatus.Submitted));
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, new ClaimsPrincipal(new ClaimsIdentity()), ExpenseStatus.Approved, null, CancellationToken.None);
        Assert.AreEqual("Unauthorized", result.ErrorCode);
    }

    [TestMethod]
    public async Task NonApproverReturnsForbidden()
    {
        ExpenseDecisionService service = Service(Repository(ExpenseStatus.Submitted));
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, Principal("employee", ExpenseHubRoles.Employee), ExpenseStatus.Approved, null, CancellationToken.None);
        Assert.AreEqual("Forbidden", result.ErrorCode);
    }

    [TestMethod]
    public async Task OwnerApproverCannotApproveOrRejectOwnExpense()
    {
        ExpenseDecisionService service = Service(Repository(ExpenseStatus.Submitted, "owner"));
        ClaimsPrincipal principal = Principal("owner", ExpenseHubRoles.Approver, ExpenseHubRoles.Employee);
        DraftServiceResult<ExpenseDraftResponse> approve = await service.DecideAsync(1, principal, ExpenseStatus.Approved, null, CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> reject = await service.DecideAsync(1, principal, ExpenseStatus.Rejected, "Valid rejection reason", CancellationToken.None);
        Assert.AreEqual("Forbidden", approve.ErrorCode);
        Assert.AreEqual("Forbidden", reject.ErrorCode);
    }

    [TestMethod]
    public async Task ApproverEmployeeCanDecideAnotherOwnersExpense()
    {
        ExpenseDecisionService service = Service(Repository(ExpenseStatus.Submitted, "owner"));
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver, ExpenseHubRoles.Employee), ExpenseStatus.Approved, null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    [DataRow((int)ExpenseStatus.Draft)]
    [DataRow((int)ExpenseStatus.Approved)]
    [DataRow((int)ExpenseStatus.Rejected)]
    [DataRow((int)ExpenseStatus.Paid)]
    public async Task NonSubmittedExpenseReturnsConflict(int status)
    {
        ExpenseDecisionService service = Service(Repository((ExpenseStatus)status));
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Approved, null, CancellationToken.None);
        Assert.AreEqual("Conflict", result.ErrorCode);
    }

    [TestMethod]
    public async Task MissingExpenseReturnsNotFound()
    {
        ExpenseDecisionService service = Service(new InMemoryQueryRepository([]));
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(99, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Approved, null, CancellationToken.None);
        Assert.AreEqual("NotFound", result.ErrorCode);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("         ")]
    [DataRow("short")]
    public async Task InvalidJustificationReturnsInvalidJustification(string? justification)
    {
        ExpenseDecisionService service = Service(Repository(ExpenseStatus.Submitted));
        DraftServiceResult<ExpenseDraftResponse> result = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Rejected, justification, CancellationToken.None);
        Assert.AreEqual("InvalidJustification", result.ErrorCode);
    }

    [TestMethod]
    public async Task JustificationAtBoundsIsAccepted()
    {
        ExpenseDecisionService service = Service(Repository(ExpenseStatus.Submitted));
        DraftServiceResult<ExpenseDraftResponse> minimum = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Rejected, new string('x', 10), CancellationToken.None);
        Assert.IsTrue(minimum.Succeeded);
        InMemoryQueryRepository secondRepository = Repository(ExpenseStatus.Submitted);
        ExpenseDecisionService secondService = Service(secondRepository);
        DraftServiceResult<ExpenseDraftResponse> maximum = await secondService.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Rejected, new string('y', 500), CancellationToken.None);
        Assert.IsTrue(maximum.Succeeded);
    }

    [TestMethod]
    public async Task RepeatedDecisionReturnsConflictWithoutDuplicateHistory()
    {
        InMemoryQueryRepository repository = Repository(ExpenseStatus.Submitted);
        ExpenseDecisionService service = Service(repository);
        await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Approved, null, CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> repeated = await service.DecideAsync(1, Principal("approver", ExpenseHubRoles.Approver), ExpenseStatus.Rejected, "Another valid reason", CancellationToken.None);
        Assert.AreEqual("Conflict", repeated.ErrorCode);
        Assert.HasCount(1, repository.Expenses[0].Histories);
    }

    private static ExpenseDecisionService Service(InMemoryQueryRepository repository) => new(repository, repository, new FixedTimeProvider(FixedNow));
    private static InMemoryQueryRepository Repository(ExpenseStatus status, string owner = "owner") => new([new Expense { Id = 1, OwnerId = owner, Description = "Valid expense description", Amount = 1, ExpenseDate = FixedNow.Date, CategoryId = 1, Category = new ExpenseCategory { Id = 1, Name = "Meals" }, Status = status }]);
    private static ClaimsPrincipal Principal(string id, params string[] roles) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), .. roles.Select(role => new Claim(ClaimTypes.Role, role))], "test"));
}
