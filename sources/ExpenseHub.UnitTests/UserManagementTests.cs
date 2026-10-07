using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using ExpenseHub.Api.Users;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>
/// Testes de registro e gerenciamento de roles no ExpenseHub.
/// </summary>
[TestClass]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Identity managers share the in-memory store for each unit test.")]
public sealed class UserRegistrationTests
{
    /// <summary>
    /// Verifica que o cadastro cria usuário sem roles.
    /// </summary>
    [TestMethod]
    public async Task RegisterCreatesUserWithoutAnyRole()
    {
        InMemoryIdentityStore store = new();
        UserRegistrationService service = new(IdentityTestFactory.CreateUserManager(store));
        string password = SyntheticValues.CreatePassword();

        ServiceResult<RegisterResponse> result = await service.RegisterAsync(
            new RegisterRequest { Email = SyntheticValues.CreateEmail(), Password = password },
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Value);
        ApplicationUser user = store.Users.Values.Single();
        Assert.AreEqual(user.Id, result.Value!.Id);
        IList<string> roles = await IdentityTestFactory.CreateUserManager(store).GetRolesAsync(user);
        Assert.IsEmpty(roles, "Registration must not grant any role.");
    }

    /// <summary>
    /// Verifica que o cadastro não promove usuário com roles privilegiadas.
    /// </summary>
    [TestMethod]
    public async Task RegisterIgnoresPrivilegedFieldsAndDoesNotPromoteUser()
    {
        InMemoryIdentityStore store = new();
        UserRegistrationService service = new(IdentityTestFactory.CreateUserManager(store));

        ServiceResult<RegisterResponse> result = await service.RegisterAsync(
            new RegisterRequest { Email = SyntheticValues.CreateEmail(), Password = SyntheticValues.CreatePassword() },
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        ApplicationUser user = store.Users.Values.Single();
        IList<string> roles = await IdentityTestFactory.CreateUserManager(store).GetRolesAsync(user);
        Assert.IsFalse(roles.Contains(ExpenseHubRoles.Employee, StringComparer.Ordinal), "Registration must not grant Employee automatically.");
        Assert.IsFalse(roles.Contains(ExpenseHubRoles.Admin, StringComparer.Ordinal), "Registration must not grant Admin automatically.");
    }

    /// <summary>
    /// Verifica que o cadastro rejeita e-mail duplicado.
    /// </summary>
    [TestMethod]
    public async Task RegisterRejectsDuplicateEmail()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        UserRegistrationService service = new(userManager);
        string email = SyntheticValues.CreateEmail();
        string password = SyntheticValues.CreatePassword();

        await userManager.CreateAsync(new ApplicationUser { UserName = email, Email = email }, password);
        ServiceResult<RegisterResponse> result = await service.RegisterAsync(
            new RegisterRequest { Email = email, Password = SyntheticValues.CreatePassword() },
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("IdentityError", result.Error!.Code);
    }

    /// <summary>
    /// Verifica que o cadastro sinaliza falha do Identity para senha inválida.
    /// </summary>
    [TestMethod]
    public async Task RegisterSurfacesIdentityFailureForInvalidPassword()
    {
        InMemoryIdentityStore store = new();
        UserRegistrationService service = new(IdentityTestFactory.CreateUserManager(store));

        ServiceResult<RegisterResponse> result = await service.RegisterAsync(
            new RegisterRequest { Email = SyntheticValues.CreateEmail(), Password = "Aa1!" },
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("IdentityError", result.Error!.Code);
        Assert.IsEmpty(store.Users);
    }

    /// <summary>
    /// Verifica que o contrato RegisterRequest rejeita campos privilegiados.
    /// </summary>
    [TestMethod]
    public void RegisterRequestRejectsPrivilegedFieldsByContract()
    {
        RegisterRequest request = new()
        {
            Email = SyntheticValues.CreateEmail(),
            Password = SyntheticValues.CreatePassword()
        };

        List<string> propertyNames = typeof(RegisterRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.HasCount(2, propertyNames);
        CollectionAssert.Contains(propertyNames, "Email");
        CollectionAssert.Contains(propertyNames, "Password");
    }
}

/// <summary>
/// Testes de serviço administrativo de roles.
/// </summary>
[TestClass]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Identity managers share the in-memory store for each unit test.")]
public sealed class AdminUserServiceTests
{
    /// <summary>
    /// Verifica que roles desconhecidas são rejeitadas antes de qualquer alteração.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesRejectsUnknownRolesBeforeAnyChange()
    {
        InMemoryIdentityStore store = new();
        AdminUserService service = new(IdentityTestFactory.CreateUserManager(store));
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee, "SuperAdmin"] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("UnknownRole", result.Error!.Code);
        IList<string> roles = await IdentityTestFactory.CreateUserManager(store).GetRolesAsync(target);
        Assert.IsEmpty(roles);
    }

    /// <summary>
    /// Verifica que lista nula de roles rejeita entrada.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesRejectsNullRoleList()
    {
        InMemoryIdentityStore store = new();
        AdminUserService service = new(IdentityTestFactory.CreateUserManager(store));
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = null },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("InvalidRequest", result.Error!.Code);
    }

    /// <summary>
    /// Verifica que role em branco é rejeitada.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesRejectsBlankRoleEntry()
    {
        InMemoryIdentityStore store = new();
        AdminUserService service = new(IdentityTestFactory.CreateUserManager(store));
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [" "] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("InvalidRole", result.Error!.Code);
    }

    /// <summary>
    /// Verifica que usuário inexistente retorna 404.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesReturnsNotFoundForUnknownUser()
    {
        InMemoryIdentityStore store = new();
        AdminUserService service = new(IdentityTestFactory.CreateUserManager(store));

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            "missing-id",
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("NotFound", result.Error!.Code);
    }

    /// <summary>
    /// Verifica que roles acumuladas são atribuídas corretamente.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesAssignsAccumulatedRoles()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee, ExpenseHubRoles.Approver] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEquivalent(
            new[] { ExpenseHubRoles.Employee, ExpenseHubRoles.Approver },
            result.Value!.Roles.ToArray());
    }

    /// <summary>
    /// Verifica que substituição completa remove roles antigas.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesSubstitutesCompleteSetAndRemovesOldRoles()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());

        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Employee);
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Finance);

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Approver] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEquivalent(new[] { ExpenseHubRoles.Approver }, result.Value!.Roles.ToArray());
    }

    /// <summary>
    /// Verifica que lista vazia remove todas as roles.
    /// </summary>
    [TestMethod]
    public async Task ReplaceRolesWithEmptyListRemovesAllRoles()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Employee);

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsEmpty(result.Value!.Roles);
    }

    /// <summary>
    /// Verifica que repetição do mesmo conjunto não altera estado.
    /// </summary>
    [TestMethod]
    public async Task RepeatingSameRoleSetIsANoOp()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Employee);

        ServiceResult<UserSummary> first = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        ServiceResult<UserSummary> second = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(second.Succeeded);
        CollectionAssert.AreEquivalent(first.Value!.Roles.ToArray(), second.Value!.Roles.ToArray());
    }

    /// <summary>
    /// Verifica que Admin não pode remover sua própria role Admin.
    /// </summary>
    [TestMethod]
    public async Task AdminCannotRemoveOwnAdminRole()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser admin = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(admin, ExpenseHubRoles.Admin);
        await userManager.AddToRoleAsync(admin, ExpenseHubRoles.Employee);

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            admin.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee] },
            CreatePrincipal(admin.Id),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("SelfAdminRemoval", result.Error!.Code);
        IList<string> roles = await userManager.GetRolesAsync(admin);
        Assert.IsTrue(roles.Contains(ExpenseHubRoles.Admin, StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifica que Admin pode manter sua role Admin e alterar outras roles.
    /// </summary>
    [TestMethod]
    public async Task AdminCanKeepOwnAdminRoleAndReplaceOtherOwnRoles()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser admin = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(admin, ExpenseHubRoles.Admin);
        await userManager.AddToRoleAsync(admin, ExpenseHubRoles.Finance);

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            admin.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Admin, ExpenseHubRoles.Auditor] },
            CreatePrincipal(admin.Id),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEquivalent(
            new[] { ExpenseHubRoles.Admin, ExpenseHubRoles.Auditor },
            result.Value!.Roles.ToArray());
    }

    /// <summary>
    /// Verifica que listagem de usuários retorna DTO sem campos sensíveis.
    /// </summary>
    [TestMethod]
    public async Task GetUsersReturnsSummaryWithoutSensitiveFields()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Employee);

        IReadOnlyList<UserSummary> users = await service.GetUsersAsync(CancellationToken.None);

        Assert.HasCount(1, users);
        Assert.AreEqual(target.Id, users[0].Id);
        Assert.AreEqual(target.Email, users[0].Email);
        Assert.HasCount(1, users[0].Roles);

        List<string> propertyNames = typeof(UserSummary)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.HasCount(3, propertyNames);
        CollectionAssert.Contains(propertyNames, "Id");
        CollectionAssert.Contains(propertyNames, "Email");
        CollectionAssert.Contains(propertyNames, "Roles");
    }

    /// <summary>
    /// Verifica que falha na atualização não deixa resultado parcial.
    /// </summary>
    [TestMethod]
    public async Task IdentityFailureDuringReplacementDoesNotLeavePartialResult()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Employee);
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Approver);
        store.FailOnAddRole = ExpenseHubRoles.Finance;

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Finance] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("IdentityError", result.Error!.Code);
        IList<string> roles = await userManager.GetRolesAsync(target);
        CollectionAssert.AreEquivalent(
            new[] { ExpenseHubRoles.Employee, ExpenseHubRoles.Approver },
            roles.ToArray());
    }

    /// <summary>
    /// Verifica que falha na remoção não deixa resultado parcial.
    /// </summary>
    [TestMethod]
    public async Task IdentityFailureDuringRemovalDoesNotLeavePartialResult()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        AdminUserService service = new(userManager);
        ApplicationUser target = await CreateUserAsync(store, SyntheticValues.CreateEmail());
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Employee);
        await userManager.AddToRoleAsync(target, ExpenseHubRoles.Approver);
        store.FailOnRemoveRole = ExpenseHubRoles.Approver;

        ServiceResult<UserSummary> result = await service.ReplaceRolesAsync(
            target.Id,
            new UpdateRolesRequest { Roles = [ExpenseHubRoles.Employee] },
            CreatePrincipal("other-admin-id"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("IdentityError", result.Error!.Code);
        IList<string> roles = await userManager.GetRolesAsync(target);
        CollectionAssert.AreEquivalent(
            new[] { ExpenseHubRoles.Employee, ExpenseHubRoles.Approver },
            roles.ToArray());
    }

    /// <summary>
    /// Cria um usuário no store para testes.
    /// </summary>
    private static async Task<ApplicationUser> CreateUserAsync(InMemoryIdentityStore store, string email)
    {
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        ApplicationUser user = new() { UserName = email, Email = email };
        IdentityResult result = await userManager.CreateAsync(user, SyntheticValues.CreatePassword());
        Assert.IsTrue(result.Succeeded);
        return user;
    }

    /// <summary>
    /// Cria um principalClaimsPrincipal para simular o Admin autenticado.
    /// </summary>
    private static ClaimsPrincipal CreatePrincipal(string userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            "test-authentication"));
    }
}
