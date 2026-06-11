using Microsoft.AspNetCore.Authorization;
using Sweeprr.API.Models;

namespace Sweeprr.API.Auth;

/// <summary>
/// Authorizes <see cref="ScopeRequirement"/>. JWT-authenticated human users (role Admin —
/// the only role Sweeprr has) always satisfy any scope. API-key principals must carry a
/// matching "scope" claim, or the blanket "admin" scope.
/// </summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        if (context.User.IsInRole(nameof(UserRole.Admin))
            || context.User.HasClaim(ApiKeyClaims.Scope, requirement.Scope)
            || context.User.HasClaim(ApiKeyClaims.Scope, ApiKeyScopes.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
