using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace ExpenseHub.Api.Auth;

internal sealed class LoginService : ILoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _tokenService;

    public LoginService(UserManager<ApplicationUser> userManager, IJwtTokenService tokenService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
    }

    public async Task<LoginResponse?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return null;
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        return _tokenService.CreateToken(user, roles);
    }
}
