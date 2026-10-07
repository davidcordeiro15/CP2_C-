using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Configuration;
using ExpenseHub.Api.Initialization;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>
/// Testes do seed inicial de Admin.
/// </summary>
[TestClass]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Identity managers share the in-memory store for each unit test.")]
public sealed class AdminSeederTests
{
    private static SeedAdminOptions ValidOptions => new()
    {
        Email = "admin@example.com",
        Password = CreateSyntheticPassword(),
        Enabled = true
    };

    private static string CreateSyntheticPassword() => $"Synthetic-{Guid.NewGuid():N}-Aa1!";

    /// <summary>
    /// Valida criação inicial de roles e Admin.
    /// </summary>
    [TestMethod]
    public async Task SeedCreatesAllRolesAndOnlyConfiguredAdmin()
    {
        InMemoryIdentityStore store = new();
        AdminSeeder seeder = CreateSeeder(store);

        await seeder.SeedAsync(CancellationToken.None);

        CollectionAssert.AreEquivalent(ExpenseHubRoles.All, store.Roles.Values.Select(role => role.Name).ToArray());
        Assert.HasCount(1, store.Users);
        ApplicationUser admin = store.Users.Values.Single();
        Assert.AreEqual(ValidOptions.Email, admin.Email);
        CollectionAssert.AreEqual(new[] { ExpenseHubRoles.Admin }, (await IdentityTestFactory.CreateUserManager(store).GetRolesAsync(admin)).ToArray());
    }

    /// <summary>
    /// Testa idempotência do seeder.
    /// </summary>
    /// <summary>
    /// Valida que o seed repetido é idempotente.
    /// </summary>
    [TestMethod]
    public async Task RepeatedSeedDoesNotRecreateRolesOrResetAdmin()
    {
        InMemoryIdentityStore store = new();
        AdminSeeder seeder = CreateSeeder(store);
        await seeder.SeedAsync(CancellationToken.None);
        ApplicationUser admin = store.Users.Values.Single();
        string passwordHash = admin.PasswordHash!;
        int roleCount = store.Roles.Count;

        await seeder.SeedAsync(CancellationToken.None);

        Assert.HasCount(1, store.Users);
        Assert.HasCount(roleCount, store.Roles);
        Assert.AreEqual(passwordHash, store.Users.Values.Single().PasswordHash);
    }

    /// <summary>
    /// Testa conflito com e-mail existente.
    /// </summary>
    /// <summary>
    /// Valida conflito com conta preexistente comum.
    /// </summary>
    [TestMethod]
    public async Task ExistingNonAdminAccountIsConflictAndIsNotPromoted()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        ApplicationUser existing = new() { UserName = ValidOptions.Email, Email = ValidOptions.Email };
        Assert.IsTrue((await userManager.CreateAsync(existing, CreateSyntheticPassword())).Succeeded);
        AdminSeeder seeder = CreateSeeder(store);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => seeder.SeedAsync(CancellationToken.None));

        StringAssert.Contains(exception.Message, "existing non-Admin");
        Assert.IsFalse(await userManager.IsInRoleAsync(existing, ExpenseHubRoles.Admin));
    }

    /// <summary>
    /// Testa falha no Identity.
    /// </summary>
    /// <summary>
    /// Valida propagação de erro de configuração do Identity.
    /// </summary>
    [TestMethod]
    public async Task IdentityFailureIsPropagated()
    {
        InMemoryIdentityStore store = new();
        AdminSeeder seeder = CreateSeeder(store, new SeedAdminOptions { Email = string.Empty, Password = string.Empty, Enabled = true });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => seeder.SeedAsync(CancellationToken.None));
    }

    private static AdminSeeder CreateSeeder(InMemoryIdentityStore store, SeedAdminOptions? options = null)
    {
        return new AdminSeeder(
            IdentityTestFactory.CreateRoleManager(store),
            IdentityTestFactory.CreateUserManager(store),
            Options.Create(options ?? ValidOptions));
    }
}
