using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Configuration;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using ExpenseHub.UnitTests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>
/// Testes de autenticação e JWT.
/// </summary>
[TestClass]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Identity managers share the in-memory store for each unit test.")]
public sealed class IdentityAuthenticationTests
{
    private static string CreateSyntheticPassword() => $"Synthetic-{Guid.NewGuid():N}-Aa1!";
    private static string CreateSigningKey() => Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());

    private static readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "ExpenseHub.Tests",
        Audience = "ExpenseHub.Tests.Client",
        SigningKey = CreateSigningKey(),
        ExpirationMinutes = 10
    };

    /// <summary>
    /// Valida que credenciais inválidas não emitem token.
    /// </summary>
    [TestMethod]
    public async Task InvalidLoginDoesNotIssueToken()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        ApplicationUser user = new() { UserName = "user@example.com", Email = "user@example.com" };
        string storedPassword = CreateSyntheticPassword();
        Assert.IsTrue((await userManager.CreateAsync(user, storedPassword)).Succeeded);
        LoginService service = new(userManager, CreateTokenService());

        LoginResponse? response = await service.AuthenticateAsync(
            new LoginRequest { Email = "user@example.com", Password = storedPassword + "wrong" },
            CancellationToken.None);

        Assert.IsNull(response);
    }

    /// <summary>
    /// Testa login válido.
    /// </summary>
    /// <summary>
    /// Valida token emitido para credenciais válidas.
    /// </summary>
    [TestMethod]
    public async Task ValidLoginIssuesTokenWithIdentifierAndRoles()
    {
        InMemoryIdentityStore store = new();
        UserManager<ApplicationUser> userManager = IdentityTestFactory.CreateUserManager(store);
        ApplicationUser user = new() { UserName = "user@example.com", Email = "user@example.com" };
        string credential = CreateSyntheticPassword();
        Assert.IsTrue((await userManager.CreateAsync(user, credential)).Succeeded);
        Assert.IsTrue((await userManager.AddToRoleAsync(user, ExpenseHubRoles.Employee)).Succeeded);
        Assert.IsTrue((await userManager.AddToRoleAsync(user, ExpenseHubRoles.Approver)).Succeeded);
        LoginService service = new(userManager, CreateTokenService());

        LoginResponse? response = await service.AuthenticateAsync(
            new LoginRequest { Email = "user@example.com", Password = credential },
            CancellationToken.None);

        Assert.IsNotNull(response);
        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.AreEqual(user.Id, token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        List<string> roleValues = token.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();
        Assert.HasCount(2, roleValues);
        foreach (var expected in new[] { ExpenseHubRoles.Employee, ExpenseHubRoles.Approver })
        {
            StringComparer comp = StringComparer.OrdinalIgnoreCase;
            Assert.IsTrue(roleValues.Any(v => comp.Equals(v, expected)),
                $"Expected role '{expected}' not found in actual roles {string.Join(", ", roleValues)}");
        }

        ValidateToken(response.AccessToken, token.ValidTo);
    }

    /// <summary>
    /// Testa as propriedades do token.
    /// </summary>
    /// <summary>
    /// Testa as propriedades do token.
    /// </summary>
    /// <summary>
    /// Valida assinatura, emissor, destinatário e expiração.
    /// </summary>
    [TestMethod]
    public void TokenUsesConfiguredSignatureIssuerAudienceAndExpiration()
    {
        JwtTokenService service = CreateTokenService();
        ApplicationUser user = new() { Id = "stable-user-id", Email = "user@example.com" };

        LoginResponse response = service.CreateToken(user, [ExpenseHubRoles.Employee]);
        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.AreEqual(_jwtOptions.Issuer, token.Issuer);
        CollectionAssert.Contains(token.Audiences.ToArray(), _jwtOptions.Audience);
        Assert.AreEqual(600, response.ExpiresIn);
        Assert.IsTrue(token.ValidTo > token.ValidFrom);
        ValidateToken(response.AccessToken, token.ValidTo);
    }

    private static JwtTokenService CreateTokenService()
    {
        return new JwtTokenService(
            Options.Create(_jwtOptions),
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    private static void ValidateToken(string encodedToken, DateTime validTo)
    {
        TokenValidationParameters parameters = new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_jwtOptions.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = _jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(11),
            LifetimeValidator = (_, expires, _, _) => expires == validTo
        };
        new JwtSecurityTokenHandler().ValidateToken(encodedToken, parameters, out _);
    }
}
