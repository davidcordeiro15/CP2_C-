using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Api.Auth;

internal interface ILoginService
{
    Task<LoginResponse?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken);
}
