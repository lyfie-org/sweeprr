using Microsoft.AspNetCore.DataProtection;
using Sweeprr.API.Services;

namespace Sweeprr.API.Configuration;

public static class DataProtectionExtensions
{
    public static IServiceCollection AddSweeprrDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configDir = configuration["ConfigDir"] ?? "/config";
        var keysDir = Path.Combine(configDir, "keys");
        Directory.CreateDirectory(keysDir);

        services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
            .SetApplicationName("Sweeprr");

        services.AddSingleton<ISecretProtector, SecretProtector>();

        return services;
    }
}
