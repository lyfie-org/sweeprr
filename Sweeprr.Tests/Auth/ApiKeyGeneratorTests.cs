using Sweeprr.API.Auth;

namespace Sweeprr.Tests.Auth;

public class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_StartsWithExpectedPrefix()
    {
        var key = ApiKeyGenerator.Generate();
        Assert.StartsWith("spr_live_", key);
    }

    [Fact]
    public void Generate_HasExpectedLength()
    {
        var key = ApiKeyGenerator.Generate();
        // "spr_live_" (9 chars) + 32 random base62 chars
        Assert.Equal(41, key.Length);
    }

    [Fact]
    public void Generate_SuffixIsBase62()
    {
        var key = ApiKeyGenerator.Generate();
        var suffix = key["spr_live_".Length..];
        Assert.Matches("^[A-Za-z0-9]{32}$", suffix);
    }

    [Fact]
    public void Generate_ProducesUniqueKeys()
    {
        var a = ApiKeyGenerator.Generate();
        var b = ApiKeyGenerator.Generate();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var key = ApiKeyGenerator.Generate();
        Assert.Equal(ApiKeyGenerator.Hash(key), ApiKeyGenerator.Hash(key));
    }

    [Fact]
    public void Hash_DiffersForDifferentKeys()
    {
        var a = ApiKeyGenerator.Generate();
        var b = ApiKeyGenerator.Generate();
        Assert.NotEqual(ApiKeyGenerator.Hash(a), ApiKeyGenerator.Hash(b));
    }

    [Fact]
    public void Hash_IsLowercaseHexSha256()
    {
        var key = ApiKeyGenerator.Generate();
        var hash = ApiKeyGenerator.Hash(key);

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_NeverEqualsRawKey()
    {
        var key = ApiKeyGenerator.Generate();
        Assert.NotEqual(key, ApiKeyGenerator.Hash(key));
    }

    [Fact]
    public void Mask_HasExpectedFormat()
    {
        var key = ApiKeyGenerator.Generate();
        var masked = ApiKeyGenerator.Mask(key);

        Assert.Equal($"spr_live_••••••••{key[^4..]}", masked);
    }

    [Fact]
    public void Mask_DoesNotRevealFullKey()
    {
        var key = ApiKeyGenerator.Generate();
        var masked = ApiKeyGenerator.Mask(key);

        Assert.DoesNotContain(key, masked);
    }
}
