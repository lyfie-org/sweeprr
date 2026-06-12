using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sweeprr.API.Auth;
using Sweeprr.API.Services;

namespace Sweeprr.API.Configuration;

public static class AuthExtensions
{
    public static IServiceCollection AddSweeprrAuth(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<JwtKeyProvider>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddJwtBearer("ExtensionPortal", _ => { })
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });

        // Configure JwtBearerOptions via factory so JwtKeyProvider is properly injected —
        // avoids the BuildServiceProvider() anti-pattern in inline lambdas.
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>>(sp =>
        {
            var keyProvider = sp.GetRequiredService<JwtKeyProvider>();
            return new ConfigureNamedOptions<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = "sweeprr",
                        ValidateAudience = true,
                        ValidAudience = "sweeprr",
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ClockSkew = TimeSpan.FromMinutes(2),
                        IssuerSigningKeyResolver = (_, _, _, _) => [keyProvider.GetKey()]
                    };
                });
        });

        // "ExtensionPortal" is a separate named scheme/issuer/audience so extension-portal
        // tokens (Story 10.4) can never authenticate against admin/API-key-gated endpoints,
        // and vice versa, even though both are signed with the same JwtKeyProvider key.
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>>(sp =>
        {
            var keyProvider = sp.GetRequiredService<JwtKeyProvider>();
            return new ConfigureNamedOptions<JwtBearerOptions>(
                "ExtensionPortal",
                options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = "sweeprr-extension-portal",
                        ValidateAudience = true,
                        ValidAudience = "sweeprr-extension-portal",
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ClockSkew = TimeSpan.FromMinutes(2),
                        IssuerSigningKeyResolver = (_, _, _, _) => [keyProvider.GetKey()]
                    };
                });
        });

        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        // Global policy: all endpoints require auth (JWT or Sweeprr API key) unless
        // decorated with [AllowAnonymous].
        services.AddAuthorization(options =>
        {
            var authenticatedUser = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
                .RequireAuthenticatedUser()
                .Build();

            options.DefaultPolicy = authenticatedUser;
            options.FallbackPolicy = authenticatedUser;

            // Scope-gated policies for sensitive endpoints. JWT-authenticated admins
            // (the only human role) always satisfy these — see ScopeAuthorizationHandler.
            options.AddPolicy("ReadSweep", p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(ApiKeyScopes.ReadSweep)));

            options.AddPolicy("WriteSweep", p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(ApiKeyScopes.WriteSweep)));

            options.AddPolicy("ExecuteSweep", p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(ApiKeyScopes.ExecuteSweep)));

            options.AddPolicy("AdminOnly", p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(ApiKeyScopes.Admin)));

            // Isolated from the admin/API-key schemes above — only an "ExtensionPortal"-issued
            // token can satisfy this policy (Story 10.4).
            options.AddPolicy("ExtensionPortal", p => p
                .AddAuthenticationSchemes("ExtensionPortal")
                .RequireAuthenticatedUser());
        });

        return services;
    }
}
