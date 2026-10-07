using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Configuration;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ExpenseHub.Api.Initialization;

internal interface IAdminSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}

internal sealed class AdminSeeder : IAdminSeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SeedAdminOptions _options;

    public AdminSeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<SeedAdminOptions> options)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Enable the admin seed explicitly with SeedAdmin:Enabled=true before running the seed.");
        }

        foreach (string role in ExpenseHubRoles.All)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await EnsureSucceededAsync(await _roleManager.CreateAsync(new IdentityRole(role)), $"create role '{role}'");
            }
        }

        ApplicationUser? user = await _userManager.FindByEmailAsync(_options.Email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = _options.Email,
                Email = _options.Email,
                EmailConfirmed = true
            };
            await EnsureSucceededAsync(await _userManager.CreateAsync(user, _options.Password), "create initial admin");
            await EnsureSucceededAsync(await _userManager.AddToRoleAsync(user, ExpenseHubRoles.Admin), "assign Admin role to initial admin");
            return;
        }

        if (!await _userManager.IsInRoleAsync(user, ExpenseHubRoles.Admin))
        {
            throw new InvalidOperationException("The configured initial admin email already belongs to an existing non-Admin account.");
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);

        if (roles.Any(role => !string.Equals(role, ExpenseHubRoles.Admin, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The configured initial admin account must belong exclusively to the Admin role.");
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Email) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("Configure SeedAdmin:Email and SeedAdmin:Password before running the seed.");
        }
    }

    private static Task EnsureSucceededAsync(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return Task.CompletedTask;
        }

        string errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Unable to {operation}: {errors}");
    }
}
