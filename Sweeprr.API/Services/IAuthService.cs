using Sweeprr.API.Dtos.Auth;

namespace Sweeprr.API.Services;

public interface IAuthService
{
    Task<bool> IsFirstRunAsync();
    Task<AuthTokenResponse> SetupAsync(string username, string password);
    Task<AuthTokenResponse?> LoginAsync(string username, string password);
}
