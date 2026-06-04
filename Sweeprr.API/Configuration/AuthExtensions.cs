using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sweeprr.API.Services;

namespace Sweeprr.API.Configuration;

public static class AuthExtensions
{
    public static IServiceCollection AddSweeprrAuth(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<JwtKeyProvider>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

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

        // Global policy: all endpoints require auth unless decorated with [AllowAnonymous].
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
