using System.Collections.Generic;
using ExpenseHub.Api.Models;

namespace ExpenseHub.Api.Auth;

internal interface IJwtTokenService
{
    LoginResponse CreateToken(ApplicationUser user, IList<string> roles);
}
