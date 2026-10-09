using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Models;
using ExpenseHub.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Users;

internal interface IAdminUserService
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken);

    Task<ServiceResult<UserSummary>> ReplaceRolesAsync(string userId, UpdateRolesRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken);
}

internal sealed class AdminUserService : IAdminUserService
{
    private static readonly HashSet<string> _allowedRoles = new(ExpenseHubRoles.All, StringComparer.OrdinalIgnoreCase);
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ExpenseHubDbContext? _dbContext;

    public AdminUserService(UserManager<ApplicationUser> userManager, ExpenseHubDbContext? dbContext = null)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken)
    {
        List<UserSummary> users = [];
        List<ApplicationUser> userList = await _userManager.Users.ToListAsync(cancellationToken);

        foreach (ApplicationUser user in userList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IList<string> roles = await _userManager.GetRolesAsync(user);
            users.Add(new UserSummary(user.Id, user.Email ?? string.Empty, roles.ToArray()));
        }

        return users;
    }

    public async Task<ServiceResult<UserSummary>> ReplaceRolesAsync(string userId, UpdateRolesRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        ServiceError? validationError = ValidateRoles(request, out string[] requestedRoles);

        if (validationError is not null)
        {
            return ServiceResult<UserSummary>.Failure(validationError.Code, validationError.Message);
        }

        ApplicationUser? targetUser = await _userManager.FindByIdAsync(userId);

        if (targetUser is null)
        {
            return ServiceResult<UserSummary>.Failure("NotFound", "User was not found.");
        }

        IList<string> currentRoles = await _userManager.GetRolesAsync(targetUser);

        bool removesOwnAdminRole = IsCurrentUser(currentUser, targetUser) &&
            currentRoles.Contains(ExpenseHubRoles.Admin, StringComparer.OrdinalIgnoreCase) &&
            !requestedRoles.Contains(ExpenseHubRoles.Admin, StringComparer.OrdinalIgnoreCase);

        if (removesOwnAdminRole)
        {
            return ServiceResult<UserSummary>.Failure("SelfAdminRemoval", "The authenticated Admin cannot remove their own Admin role.");
        }

        string[] rolesToRemove = currentRoles.Except(requestedRoles, StringComparer.Ordinal).ToArray();
        string[] rolesToAdd = requestedRoles.Except(currentRoles, StringComparer.Ordinal).ToArray();

        if (rolesToRemove.Length == 0 && rolesToAdd.Length == 0)
        {
            return ServiceResult<UserSummary>.Success(await CreateSummaryAsync(targetUser));
        }

        if (_dbContext is not null)
        {
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            ServiceResult<UserSummary> transactionResult = await ApplyRoleChangesAsync(targetUser, rolesToRemove, rolesToAdd, compensateOnFailure: false);

            if (transactionResult.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return transactionResult;
        }

        return await ApplyRoleChangesAsync(targetUser, rolesToRemove, rolesToAdd, compensateOnFailure: true);
    }

    private async Task<ServiceResult<UserSummary>> ApplyRoleChangesAsync(ApplicationUser targetUser, string[] rolesToRemove, string[] rolesToAdd, bool compensateOnFailure)
    {
        List<string> removedRoles = [];

        foreach (string role in rolesToRemove)
        {
            IdentityResult result;

            try
            {
                result = await _userManager.RemoveFromRoleAsync(targetUser, role);
            }
            catch (Exception)
            {
                if (compensateOnFailure)
                {
                    await RollbackRemovedRolesAsync(targetUser, removedRoles);
                }

                return ServiceResult<UserSummary>.Failure("IdentityError", "Unable to update roles.");
            }

            if (!result.Succeeded)
            {
                if (compensateOnFailure)
                {
                    await RollbackRemovedRolesAsync(targetUser, removedRoles);
                }

                return ServiceResult<UserSummary>.Failure("IdentityError", FormatErrors(result.Errors));
            }

            removedRoles.Add(role);
        }

        List<string> addedRoles = [];

        foreach (string role in rolesToAdd)
        {
            IdentityResult result;

            try
            {
                result = await _userManager.AddToRoleAsync(targetUser, role);
            }
            catch (Exception)
            {
                if (compensateOnFailure)
                {
                    await RollbackAddedRolesAsync(targetUser, addedRoles);
                    await RollbackRemovedRolesAsync(targetUser, removedRoles);
                }

                return ServiceResult<UserSummary>.Failure("IdentityError", "Unable to update roles.");
            }

            if (!result.Succeeded)
            {
                if (compensateOnFailure)
                {
                    await RollbackAddedRolesAsync(targetUser, addedRoles);
                    await RollbackRemovedRolesAsync(targetUser, removedRoles);
                }

                return ServiceResult<UserSummary>.Failure("IdentityError", FormatErrors(result.Errors));
            }

            addedRoles.Add(role);
        }

        return ServiceResult<UserSummary>.Success(await CreateSummaryAsync(targetUser));
    }

    private static ServiceError? ValidateRoles(UpdateRolesRequest request, out string[] roles)
    {
        roles = [];

        if (request.Roles is null)
        {
            return new ServiceError("InvalidRequest", "The roles property is required.");
        }

        if (request.Roles.Any(string.IsNullOrWhiteSpace))
        {
            return new ServiceError("InvalidRole", "Roles cannot be empty.");
        }

        string[] unknownRoles = request.Roles
            .Where(role => !_allowedRoles.Contains(role))
            .ToArray();

        if (unknownRoles.Length > 0)
        {
            return new ServiceError("UnknownRole", "Only known ExpenseHub roles are allowed.");
        }

        roles = request.Roles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(role => ExpenseHubRoles.All.First(knownRole => string.Equals(knownRole, role, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return null;
    }

    private async Task RollbackRemovedRolesAsync(ApplicationUser user, List<string> removedRoles)
    {
        foreach (string role in removedRoles)
        {
            try
            {
                await _userManager.AddToRoleAsync(user, role);
            }
            catch
            {
            }
        }
    }

    private async Task RollbackAddedRolesAsync(ApplicationUser user, List<string> addedRoles)
    {
        foreach (string role in addedRoles)
        {
            try
            {
                await _userManager.RemoveFromRoleAsync(user, role);
            }
            catch
            {
            }
        }
    }

    private async Task<UserSummary> CreateSummaryAsync(ApplicationUser user)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);
        return new UserSummary(user.Id, user.Email ?? string.Empty, roles.ToArray());
    }

    private static bool IsCurrentUser(ClaimsPrincipal currentUser, ApplicationUser targetUser)
    {
        string? currentUserId = currentUser.FindFirstValue(ClaimTypes.NameIdentifier) ?? currentUser.FindFirstValue("sub");
        return string.Equals(currentUserId, targetUser.Id, StringComparison.Ordinal);
    }

    private static string FormatErrors(IEnumerable<IdentityError> errors)
    {
        return string.Join("; ", errors.Select(error => error.Description));
    }
}
