using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sweeprr.API.Data;

namespace Sweeprr.API.Services;

/// <summary>
/// Provides the JWT signing key from GlobalSettings at validation time,
/// caching it so that DB reads are not issued on every token check.
/// Call InvalidateCache() after a secret rotation.
/// </summary>
public class JwtKeyProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private SecurityKey? _cached;

    public JwtKeyProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public SecurityKey GetKey()
    {
        if (_cached is not null)
            return _cached;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var secret = db.GlobalSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => s.JwtSecret).FirstOrDefault()
            ?? throw new InvalidOperationException("GlobalSettings not seeded — cannot resolve JWT key.");

        _cached = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        return _cached;
    }

    public void InvalidateCache() => _cached = null;
}
