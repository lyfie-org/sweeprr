using Microsoft.AspNetCore.DataProtection;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Services;

public class SecretProtectorTests
{
    private readonly ISecretProtector _protector;

    public SecretProtectorTests()
    {
        var keysDir = Path.Combine(Path.GetTempPath(), $"sweeprr_dp_{Guid.NewGuid()}");
        Directory.CreateDirectory(keysDir);

        var provider = DataProtectionProvider.Create(new DirectoryInfo(keysDir));
        _protector = new SecretProtector(provider);
    }

    [Fact]
    public void Protect_Returns_Ciphertext_Not_Equal_To_Plaintext()
    {
        var plaintext = "super-secret-api-key-abc123";
        var ciphertext = _protector.Protect(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.NotEmpty(ciphertext);
    }

    [Fact]
    public void Unprotect_Decrypts_Correctly()
    {
        var plaintext = "jellyfin-api-key-xyz789";
        var ciphertext = _protector.Protect(plaintext);
        var decrypted = _protector.Unprotect(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Unprotect_Invalid_Ciphertext_Returns_Null()
    {
        var result = _protector.Unprotect("not-valid-ciphertext-garbage");
        Assert.Null(result);
    }

    [Fact]
    public void Protect_Different_Plaintexts_Produce_Different_Ciphertexts()
    {
        var ct1 = _protector.Protect("key-one");
        var ct2 = _protector.Protect("key-two");

        Assert.NotEqual(ct1, ct2);
    }

    [Fact]
    public void Protect_Same_Plaintext_Twice_Produces_Different_Ciphertexts()
    {
        // Data Protection uses randomized IVs — same plaintext ≠ same ciphertext
        var ct1 = _protector.Protect("same-key");
        var ct2 = _protector.Protect("same-key");

        Assert.NotEqual(ct1, ct2);
    }
}
