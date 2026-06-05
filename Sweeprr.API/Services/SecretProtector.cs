using Microsoft.AspNetCore.DataProtection;

namespace Sweeprr.API.Services;

public class SecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Sweeprr.Secrets.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch
        {
            // Keys rotated / lost — caller must surface a re-entry prompt
            return null;
        }
    }
}
