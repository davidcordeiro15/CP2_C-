using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace ExpenseHub.Api.Users;

internal interface IUserRegistrationService
{
    Task<ServiceResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
}

internal sealed class UserRegistrationService : IUserRegistrationService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserRegistrationService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ServiceResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ApplicationUser user = new()
        {
            UserName = request.Email,
            Email = request.Email
        };

        IdentityResult result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return ServiceResult<RegisterResponse>.Failure("IdentityError", FormatErrors(result.Errors));
        }

        return ServiceResult<RegisterResponse>.Success(new RegisterResponse(user.Id, user.Email ?? request.Email));
    }

    private static string FormatErrors(IEnumerable<IdentityError> errors)
    {
        return string.Join("; ", errors.Select(error => error.Description));
    }
}
