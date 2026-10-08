using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

/// <summary>
/// Testes de criação e edição de rascunhos.
/// </summary>
[TestClass]
public sealed class ExpenseDraftServiceTests
{
    internal static readonly DateTimeOffset FixedNow = new(2030, 5, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Cria rascunho com proprietário autenticado.
    /// </summary>
    [TestMethod]
    public async Task CreateDraftUsesAuthenticatedOwnerAndDraftStatus()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        ExpenseDraftRequest request = ValidRequest();

        DraftServiceResult<ExpenseDraftResponse> result = await service.CreateAsync(request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("owner-1", result.Value!.OwnerId);
        Assert.AreEqual(ExpenseStatus.Draft, result.Value.Status);
        Assert.HasCount(1, repository.Expenses);
        Assert.AreEqual("owner-1", repository.Expenses[0].OwnerId);
        Assert.AreEqual(ExpenseStatus.Draft, repository.Expenses[0].Status);
    }

    /// <summary>
    /// Garante que campos extras não pertencem ao contrato de entrada.
    /// </summary>
    [TestMethod]
    public void DraftRequestDoesNotExposeInternalFields()
    {
        string[] properties = typeof(ExpenseDraftRequest).GetProperties().Select(property => property.Name).ToArray();
        string[] expected = new[] { "Description", "Amount", "ExpenseDate", "CategoryId" };

        CollectionAssert.AreEquivalent(expected, properties);
    }

    /// <summary>
    /// Valida limites da descrição.
    /// </summary>
    [TestMethod]
    [DataRow(10, true)]
    [DataRow(500, true)]
    [DataRow(9, false)]
    [DataRow(501, false)]
    public void DescriptionValidationHonorsLimits(int length, bool expected)
    {
        ExpenseDraftRequest request = ValidRequest();
        request.Description = new string('A', length);

        Assert.AreEqual(expected, IsValid(request));
    }

    /// <summary>
    /// Valida limites de valor.
    /// </summary>
    [TestMethod]
    [DataRow("0.01", true)]
    [DataRow("2147483647", true)]
    [DataRow("0.00", false)]
    [DataRow("2147483648", false)]
    public void AmountValidationHonorsLimits(string amount, bool expected)
    {
        ExpenseDraftRequest request = ValidRequest();
        request.Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        Assert.AreEqual(expected, IsValid(request));
    }

    /// <summary>
    /// Rejeita data futura com relógio controlado.
    /// </summary>
    [TestMethod]
    public async Task CreateRejectsFutureDate()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        ExpenseDraftRequest request = ValidRequest();
        request.ExpenseDate = FixedNow.Date.AddDays(1);

        DraftServiceResult<ExpenseDraftResponse> result = await service.CreateAsync(request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("InvalidDate", result.ErrorCode);
        Assert.IsEmpty(repository.Expenses);
    }

    /// <summary>
    /// Aceita a data de hoje com relógio controlado.
    /// </summary>
    [TestMethod]
    public async Task CreateAcceptsTodayWithControlledClock()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        ExpenseDraftRequest request = ValidRequest();
        request.ExpenseDate = FixedNow.Date;

        DraftServiceResult<ExpenseDraftResponse> result = await service.CreateAsync(request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
    }

    /// <summary>
    /// Rejeita categoria inexistente.
    /// </summary>
    [TestMethod]
    public async Task CreateRejectsMissingCategory()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        ExpenseDraftRequest request = ValidRequest();
        request.CategoryId = 999;

        DraftServiceResult<ExpenseDraftResponse> result = await service.CreateAsync(request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("InvalidCategory", result.ErrorCode);
        Assert.IsEmpty(repository.Expenses);
    }

    /// <summary>
    /// Edita rascunho próprio.
    /// </summary>
    [TestMethod]
    public async Task UpdateOwnDraftChangesEditableFields()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        Expense expense = repository.AddExpense("owner-1", ExpenseStatus.Draft);
        ExpenseDraftRequest request = ValidRequest();
        request.Description = "Updated description";
        request.Amount = 20;

        DraftServiceResult<ExpenseDraftResponse> result = await service.UpdateAsync(expense.Id, request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("owner-1", expense.OwnerId);
        Assert.AreEqual(ExpenseStatus.Draft, expense.Status);
        Assert.AreEqual("Updated description", expense.Description);
    }

    /// <summary>
    /// Bloqueia edição de outro proprietário.
    /// </summary>
    [TestMethod]
    public async Task UpdateOtherOwnerReturnsNotFound()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        Expense expense = repository.AddExpense("owner-1", ExpenseStatus.Draft);

        DraftServiceResult<ExpenseDraftResponse> result = await service.UpdateAsync(expense.Id, ValidRequest(), Principal("owner-2", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("NotFound", result.ErrorCode);
    }

    /// <summary>
    /// Bloqueia edição fora de Draft.
    /// </summary>
    [TestMethod]
    public async Task UpdateNonDraftReturnsConflict()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        Expense expense = repository.AddExpense("owner-1", ExpenseStatus.Submitted);

        DraftServiceResult<ExpenseDraftResponse> result = await service.UpdateAsync(expense.Id, ValidRequest(), Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("Conflict", result.ErrorCode);
    }

    /// <summary>
    /// Registra histórico de criação e edição com diferenças.
    /// </summary>
    [TestMethod]
    public async Task CreateAndUpdateWriteHistoryWithChanges()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        DraftServiceResult<ExpenseDraftResponse> created = await service.CreateAsync(ValidRequest(), Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);
        Expense expense = repository.Expenses.Single();
        ExpenseDraftRequest request = ValidRequest();
        request.Description = "Updated description";

        DraftServiceResult<ExpenseDraftResponse> updated = await service.UpdateAsync(created.Value!.Id, request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsTrue(updated.Succeeded);
        Assert.HasCount(2, expense.Histories);
        Assert.AreEqual("Created", expense.Histories.ElementAt(0).Action);
        Assert.AreEqual("Updated", expense.Histories.ElementAt(1).Action);
        StringAssert.Contains(expense.Histories.ElementAt(1).DraftChanges!, "Description");
    }

    /// <summary>
    /// Não cria histórico quando edição não altera dados.
    /// </summary>
    [TestMethod]
    public async Task UpdateWithoutChangesDoesNotWriteHistory()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        Expense expense = repository.AddExpense("owner-1", ExpenseStatus.Draft);

        DraftServiceResult<ExpenseDraftResponse> result = await service.UpdateAsync(expense.Id, ValidRequest(), Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsEmpty(expense.Histories);
    }

    /// <summary>
    /// Validação falha antes de gravar.
    /// </summary>
    [TestMethod]
    public async Task ValidationFailureDoesNotSave()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        ExpenseDraftRequest request = ValidRequest();
        request.CategoryId = 999;

        DraftServiceResult<ExpenseDraftResponse> result = await service.CreateAsync(request, Principal("owner-1", ExpenseHubRoles.Employee), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, repository.SaveCount);
        Assert.IsEmpty(repository.Expenses);
    }

    /// <summary>Admin isolado não cria nem edita.</summary>
    [TestMethod]
    public async Task AdminWithoutEmployeeCannotCreateOrEdit()
    {
        InMemoryExpenseDraftRepository repository = CreateRepository();
        ExpenseDraftService service = CreateService(repository);
        Expense expense = repository.AddExpense("owner-1", ExpenseStatus.Draft);
        DraftServiceResult<ExpenseDraftResponse> create = await service.CreateAsync(ValidRequest(), Principal("admin", ExpenseHubRoles.Admin), CancellationToken.None);
        DraftServiceResult<ExpenseDraftResponse> update = await service.UpdateAsync(expense.Id, ValidRequest(), Principal("admin", ExpenseHubRoles.Admin), CancellationToken.None);
        Assert.AreEqual("Forbidden", create.ErrorCode);
        Assert.AreEqual("Forbidden", update.ErrorCode);
    }

    private static ClaimsPrincipal Principal(string id, params string[] roles) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), .. roles.Select(role => new Claim(ClaimTypes.Role, role))], "test"));

    private static ExpenseDraftService CreateService(InMemoryExpenseDraftRepository repository)
    {
        return new ExpenseDraftService(repository, new FixedTimeProvider(FixedNow));
    }

    private static InMemoryExpenseDraftRepository CreateRepository()
    {
        InMemoryExpenseDraftRepository repository = new();
        repository.Categories.Add(new ExpenseCategory { Id = 1, Name = "Meals" });
        return repository;
    }

    private static ExpenseDraftRequest ValidRequest()
    {
        return new ExpenseDraftRequest
        {
            Description = "Valid draft description",
            Amount = 10,
            ExpenseDate = FixedNow.Date,
            CategoryId = 1
        };
    }

    private static bool IsValid(ExpenseDraftRequest request)
    {
        return Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true);
    }
}

internal sealed class InMemoryExpenseDraftRepository : IExpenseDraftRepository
{
    private int _nextId = 1;

    public List<Expense> Expenses { get; } = [];
    public List<ExpenseCategory> Categories { get; } = [];
    public int SaveCount { get; private set; }

    public Expense AddExpense(string ownerId, ExpenseStatus status)
    {
        ExpenseCategory category = Categories.Single();
        Expense expense = new()
        {
            Id = _nextId++,
            OwnerId = ownerId,
            Description = "Valid draft description",
            Amount = 10,
            ExpenseDate = ExpenseDraftServiceTests.FixedNow.Date,
            CategoryId = category.Id,
            Category = category,
            Status = status
        };
        Expenses.Add(expense);
        return expense;
    }

    public Task<Expense?> FindOwnedAsync(int id, string ownerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Expenses.SingleOrDefault(expense => expense.Id == id && expense.OwnerId == ownerId));
    }

    public Task<Expense?> FindOwnedReadOnlyAsync(int id, string ownerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Expenses.SingleOrDefault(expense => expense.Id == id && expense.OwnerId == ownerId));
    }

    public Task<Expense?> SubmitOwnedAsync(int id, string ownerId, DateTime submittedAtUtc, CancellationToken cancellationToken)
    {
        Expense? expense = Expenses.SingleOrDefault(item => item.Id == id && item.OwnerId == ownerId && item.Status == ExpenseStatus.Draft);
        if (expense is not null)
        {
            expense.Status = ExpenseStatus.Submitted;
        }

        return Task.FromResult(expense);
    }

    public Task<ExpenseCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken)
    {
        return Task.FromResult(Categories.SingleOrDefault(category => category.Id == id));
    }

    public Task AddAsync(Expense expense, CancellationToken cancellationToken)
    {
        expense.Id = _nextId++;
        Expenses.Add(expense);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
