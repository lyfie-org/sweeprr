using Microsoft.AspNetCore.Authorization;

namespace Sweeprr.API.Auth;

/// <summary>Requires the current principal to hold the given Sweeprr API key scope.</summary>
public sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    public string Scope { get; } = scope;
}
