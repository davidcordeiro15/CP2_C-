using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace ExpenseHub.UnitTests.Fakes;

internal sealed class InMemoryIdentityStore
    : IUserStore<ApplicationUser>,
      IUserPasswordStore<ApplicationUser>,
      IUserEmailStore<ApplicationUser>,
      IUserRoleStore<ApplicationUser>,
      IUserClaimStore<ApplicationUser>,
      IRoleStore<IdentityRole>
{
    private readonly Dictionary<string, ApplicationUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IdentityRole> _roles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _userRoles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ApplicationUser> Users => _users;
    public IReadOnlyDictionary<string, IdentityRole> Roles => _roles;

    Task<IdentityResult> IUserStore<ApplicationUser>.CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        string key = user.NormalizedUserName ?? user.UserName?.ToUpperInvariant()!;
        if (_users.ContainsKey(key))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "User already exists." }));
        }

        _users[key] = user;
        _userRoles[user.Id] = [];
        return Task.FromResult(IdentityResult.Success);
    }

    Task<IdentityResult> IUserStore<ApplicationUser>.DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _users.Remove(user.NormalizedUserName ?? user.UserName?.ToUpperInvariant()!);
        _userRoles.Remove(user.Id);
        return Task.FromResult(IdentityResult.Success);
    }

    Task<ApplicationUser?> IUserStore<ApplicationUser>.FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_users.Values.FirstOrDefault(u => u.Id == userId));
    }

    Task<ApplicationUser?> IUserStore<ApplicationUser>.FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        _users.TryGetValue(normalizedUserName, out ApplicationUser? user);
        return Task.FromResult(user);
    }

    Task<string?> IUserStore<ApplicationUser>.GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.NormalizedUserName);
    }

    Task<string> IUserStore<ApplicationUser>.GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.Id);
    }

    Task<string?> IUserStore<ApplicationUser>.GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.UserName);
    }

    Task IUserStore<ApplicationUser>.SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    Task IUserStore<ApplicationUser>.SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        user.NormalizedUserName = userName?.ToUpperInvariant();
        return Task.CompletedTask;
    }

    Task<IdentityResult> IUserStore<ApplicationUser>.UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        string key = user.NormalizedUserName ?? user.UserName?.ToUpperInvariant()!;
        if (_users.ContainsKey(key))
        {
            _users[key] = user;
        }

        return Task.FromResult(IdentityResult.Success);
    }

    Task IUserPasswordStore<ApplicationUser>.SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    Task<string?> IUserPasswordStore<ApplicationUser>.GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.PasswordHash);
    }

    Task<bool> IUserPasswordStore<ApplicationUser>.HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
    }

    Task<ApplicationUser?> IUserEmailStore<ApplicationUser>.FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return Task.FromResult(_users.Values.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail));
    }

    Task<string?> IUserEmailStore<ApplicationUser>.GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.Email);
    }

    Task<bool> IUserEmailStore<ApplicationUser>.GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.EmailConfirmed);
    }

    Task<string?> IUserEmailStore<ApplicationUser>.GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.NormalizedEmail);
    }

    Task IUserEmailStore<ApplicationUser>.SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        user.NormalizedEmail = email?.ToUpperInvariant();
        return Task.CompletedTask;
    }

    Task IUserEmailStore<ApplicationUser>.SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    Task IUserEmailStore<ApplicationUser>.SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    Task IUserRoleStore<ApplicationUser>.AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        if (!_userRoles.TryGetValue(user.Id, out HashSet<string>? roles))
        {
            roles = [];
            _userRoles[user.Id] = roles;
        }

        roles.Add(roleName.ToUpperInvariant());
        return Task.CompletedTask;
    }

    Task IUserRoleStore<ApplicationUser>.RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        if (_userRoles.TryGetValue(user.Id, out HashSet<string>? roles))
        {
            roles.Remove(roleName.ToUpperInvariant());
        }

        return Task.CompletedTask;
    }

    Task<IList<string>> IUserRoleStore<ApplicationUser>.GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _userRoles.TryGetValue(user.Id, out HashSet<string>? roles);
        return Task.FromResult<IList<string>>((roles ?? [])
            .Select(role => _roles.TryGetValue(role, out IdentityRole? identityRole) ? identityRole.Name! : role)
            .ToList());
    }

    Task<bool> IUserRoleStore<ApplicationUser>.IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        _userRoles.TryGetValue(user.Id, out HashSet<string>? roles);
        return Task.FromResult(roles?.Contains(roleName) ?? false);
    }

    Task<IList<ApplicationUser>> IUserRoleStore<ApplicationUser>.GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        List<ApplicationUser> result = _userRoles
            .Where(kv => kv.Value.Contains(roleName))
            .Select(kv => _users.Values.First(u => u.Id == kv.Key))
            .ToList();
        return Task.FromResult<IList<ApplicationUser>>(result);
    }

    Task<IList<Claim>> IUserClaimStore<ApplicationUser>.GetClaimsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        return Task.FromResult<IList<Claim>>([]);
    }

    Task IUserClaimStore<ApplicationUser>.AddClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    Task IUserClaimStore<ApplicationUser>.ReplaceClaimAsync(ApplicationUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    Task IUserClaimStore<ApplicationUser>.RemoveClaimsAsync(ApplicationUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    Task<IList<ApplicationUser>> IUserClaimStore<ApplicationUser>.GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        return Task.FromResult<IList<ApplicationUser>>([]);
    }

    Task<IdentityResult> IRoleStore<IdentityRole>.CreateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        string key = role.NormalizedName ?? role.Name?.ToUpperInvariant()!;
        if (_roles.ContainsKey(key))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Role already exists." }));
        }

        _roles[key] = role;
        return Task.FromResult(IdentityResult.Success);
    }

    Task<IdentityResult> IRoleStore<IdentityRole>.DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        _roles.Remove(role.NormalizedName ?? role.Name?.ToUpperInvariant()!);
        return Task.FromResult(IdentityResult.Success);
    }

    Task<IdentityRole?> IRoleStore<IdentityRole>.FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_roles.Values.FirstOrDefault(r => r.Id == roleId));
    }

    Task<IdentityRole?> IRoleStore<IdentityRole>.FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        _roles.TryGetValue(normalizedRoleName, out IdentityRole? role);
        return Task.FromResult(role);
    }

    Task<string?> IRoleStore<IdentityRole>.GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        return Task.FromResult(role.NormalizedName);
    }

    Task<string> IRoleStore<IdentityRole>.GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        return Task.FromResult(role.Id);
    }

    Task<string?> IRoleStore<IdentityRole>.GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        return Task.FromResult(role.Name);
    }

    Task IRoleStore<IdentityRole>.SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    Task IRoleStore<IdentityRole>.SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        role.NormalizedName = roleName?.ToUpperInvariant();
        return Task.CompletedTask;
    }

    Task<IdentityResult> IRoleStore<IdentityRole>.UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        string key = role.NormalizedName ?? role.Name?.ToUpperInvariant()!;
        if (_roles.ContainsKey(key))
        {
            _roles[key] = role;
        }

        return Task.FromResult(IdentityResult.Success);
    }

    public void Dispose()
    {
    }
}
