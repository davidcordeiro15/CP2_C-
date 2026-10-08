using System.Linq;
using System.Security.Claims;
using ExpenseHub.Api.Expenses;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Testes das regras contextuais de acesso.</summary>
[TestClass]
public sealed class ExpenseAccessMatrixTests
{
    /// <summary>Bloqueia autoaprovação com roles acumuladas.</summary>
    [TestMethod]
    public void ApproverEmployeeCannotDecideOwnSubmittedExpense()
    {
        Expense expense = CreateExpense("owner", ExpenseStatus.Submitted);
        DraftServiceResult<object> result = ExpenseWriteRules.AuthorizeDecision(Principal("owner", ExpenseHubRoles.Approver, ExpenseHubRoles.Employee), expense);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Forbidden", result.ErrorCode);
    }

    /// <summary>Bloqueia autopagamento com roles acumuladas.</summary>
    [TestMethod]
    public void FinanceEmployeeCannotPayOwnApprovedExpense()
    {
        Expense expense = CreateExpense("owner", ExpenseStatus.Approved);
        DraftServiceResult<object> result = ExpenseWriteRules.AuthorizePayment(Principal("owner", ExpenseHubRoles.Finance, ExpenseHubRoles.Employee), expense);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Forbidden", result.ErrorCode);
    }

    /// <summary>Bloqueia autorreprovação com roles acumuladas.</summary>
    [TestMethod]
    public void ApproverEmployeeCannotRejectOwnSubmittedExpense()
    {
        Expense expense = CreateExpense("owner", ExpenseStatus.Submitted);
        DraftServiceResult<object> result = ExpenseWriteRules.AuthorizeDecision(Principal("owner", ExpenseHubRoles.Approver, ExpenseHubRoles.Employee), expense);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Forbidden", result.ErrorCode);
    }

    /// <summary>Estado incorreto deve retornar conflito.</summary>
    [TestMethod]
    public void ActorWithCorrectRoleAndWrongStateGetsConflict()
    {
        Expense submitted = CreateExpense("owner", ExpenseStatus.Submitted);
        Expense approved = CreateExpense("owner", ExpenseStatus.Approved);
        DraftServiceResult<object> payment = ExpenseWriteRules.AuthorizePayment(Principal("finance", ExpenseHubRoles.Finance), submitted);
        DraftServiceResult<object> decision = ExpenseWriteRules.AuthorizeDecision(Principal("approver", ExpenseHubRoles.Approver), approved);
        Assert.AreEqual("Conflict", payment.ErrorCode);
        Assert.AreEqual("Conflict", decision.ErrorCode);
    }

    /// <summary>Auditor isolado não ganha escrita.</summary>
    [TestMethod]
    public void AuditorOnlyCannotDecideOrPay()
    {
        Expense submitted = CreateExpense("owner", ExpenseStatus.Submitted);
        Expense approved = CreateExpense("owner", ExpenseStatus.Approved);
        DraftServiceResult<object> decision = ExpenseWriteRules.AuthorizeDecision(Principal("auditor", ExpenseHubRoles.Auditor), submitted);
        DraftServiceResult<object> payment = ExpenseWriteRules.AuthorizePayment(Principal("auditor", ExpenseHubRoles.Auditor), approved);
        Assert.AreEqual("Forbidden", decision.ErrorCode);
        Assert.AreEqual("Forbidden", payment.ErrorCode);
    }

    /// <summary>União de roles permite decisão e pagamento não próprio.</summary>
    [TestMethod]
    public void AccumulatedRolesAllowNonOwnerDecisionAndPayment()
    {
        Expense submitted = CreateExpense("owner", ExpenseStatus.Submitted);
        Expense approved = CreateExpense("owner", ExpenseStatus.Approved);
        DraftServiceResult<object> decision = ExpenseWriteRules.AuthorizeDecision(Principal("actor", ExpenseHubRoles.Approver, ExpenseHubRoles.Employee), submitted);
        DraftServiceResult<object> payment = ExpenseWriteRules.AuthorizePayment(Principal("actor", ExpenseHubRoles.Finance, ExpenseHubRoles.Employee), approved);
        Assert.IsTrue(decision.Succeeded);
        Assert.IsTrue(payment.Succeeded);
    }

    private static Expense CreateExpense(string ownerId, ExpenseStatus status) => new() { OwnerId = ownerId, Status = status };
    private static ClaimsPrincipal Principal(string id, params string[] roles) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), .. roles.Select(role => new Claim(ClaimTypes.Role, role))], "test"));
}
