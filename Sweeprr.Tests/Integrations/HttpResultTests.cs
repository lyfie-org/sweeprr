using Sweeprr.API.Integrations;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Unit tests for <see cref="HttpResult{T}"/> — the safety-critical discriminated union
/// that separates transient failures (unknown existence) from definitive failures (item
/// genuinely gone) and successes.
/// </summary>
public class HttpResultTests
{
    // ── Factory helpers ──────────────────────────────────────────────────────

    [Fact]
    public void Ok_Returns_Success()
    {
        var r = HttpResult<int>.Ok(42);
        Assert.IsType<HttpResult<int>.Success>(r);
    }

    [Fact]
    public void Transient_Returns_TransientFailure()
    {
        var r = HttpResult<int>.Transient("net error");
        Assert.IsType<HttpResult<int>.TransientFailure>(r);
    }

    [Fact]
    public void Definitive_Returns_DefinitiveFailure()
    {
        var r = HttpResult<int>.Definitive(404, "not found");
        Assert.IsType<HttpResult<int>.DefinitiveFailure>(r);
    }

    // ── IsSuccess / IsTransient / IsDefinitive ───────────────────────────────

    [Fact]
    public void Success_State_Flags()
    {
        var r = HttpResult<string>.Ok("hello");
        Assert.True(r.IsSuccess);
        Assert.False(r.IsTransient);
        Assert.False(r.IsDefinitive);
        Assert.False(r.IsNotFound);
    }

    [Fact]
    public void Transient_State_Flags()
    {
        var r = HttpResult<string>.Transient("timeout");
        Assert.False(r.IsSuccess);
        Assert.True(r.IsTransient);
        Assert.False(r.IsDefinitive);
        Assert.False(r.IsNotFound);
    }

    [Fact]
    public void Definitive_State_Flags()
    {
        var r = HttpResult<string>.Definitive(401, "unauthorized");
        Assert.False(r.IsSuccess);
        Assert.False(r.IsTransient);
        Assert.True(r.IsDefinitive);
        Assert.False(r.IsNotFound);
    }

    // ── IsNotFound (safety-critical: only 404 qualifies) ────────────────────

    [Fact]
    public void IsNotFound_True_Only_For_404()
    {
        Assert.True(HttpResult<int>.Definitive(404, "").IsNotFound);
    }

    [Fact]
    public void IsNotFound_False_For_Other_4xx()
    {
        Assert.False(HttpResult<int>.Definitive(401, "").IsNotFound);
        Assert.False(HttpResult<int>.Definitive(403, "").IsNotFound);
        Assert.False(HttpResult<int>.Definitive(400, "").IsNotFound);
    }

    [Fact]
    public void IsNotFound_False_For_5xx()
    {
        Assert.False(HttpResult<int>.Definitive(500, "").IsNotFound);
    }

    [Fact]
    public void IsNotFound_False_For_Transient()
    {
        Assert.False(HttpResult<int>.Transient("").IsNotFound);
    }

    [Fact]
    public void IsNotFound_False_For_Success()
    {
        Assert.False(HttpResult<int>.Ok(0).IsNotFound);
    }

    [Fact]
    public void IsNotFound_False_For_Null_StatusCode()
    {
        // Null status code = JSON deserialization failure, not a 404.
        Assert.False(HttpResult<int>.Definitive(null, "deser failed").IsNotFound);
    }

    // ── Unwrap ───────────────────────────────────────────────────────────────

    [Fact]
    public void Unwrap_Success_Returns_Value()
    {
        Assert.Equal("hello", HttpResult<string>.Ok("hello").Unwrap());
    }

    [Fact]
    public void Unwrap_Transient_Throws_InvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(
            () => HttpResult<string>.Transient("err").Unwrap());
    }

    [Fact]
    public void Unwrap_Definitive_Throws_InvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(
            () => HttpResult<string>.Definitive(404, "nf").Unwrap());
    }

    // ── Map ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Map_Success_Transforms_Value()
    {
        var mapped = HttpResult<int>.Ok(5).Map(x => x * 10);
        var s = Assert.IsType<HttpResult<int>.Success>(mapped);
        Assert.Equal(50, s.Value);
    }

    [Fact]
    public void Map_Transient_Propagates_With_Same_Reason()
    {
        var ex = new IOException("io");
        var mapped = HttpResult<int>.Transient("io error", ex).Map(x => x.ToString());
        var t = Assert.IsType<HttpResult<string>.TransientFailure>(mapped);
        Assert.Equal("io error", t.Reason);
        Assert.Same(ex, t.Exception);
    }

    [Fact]
    public void Map_Definitive_Propagates_With_Same_StatusCode()
    {
        var mapped = HttpResult<int>.Definitive(404, "nf").Map(x => x.ToString());
        var d = Assert.IsType<HttpResult<string>.DefinitiveFailure>(mapped);
        Assert.Equal(404, d.StatusCode);
        Assert.Equal("nf", d.Reason);
    }

    [Fact]
    public void Map_Changes_Generic_Type()
    {
        HttpResult<string> mapped = HttpResult<int>.Ok(42).Map(x => x.ToString());
        Assert.True(mapped.IsSuccess);
        Assert.Equal("42", mapped.Unwrap());
    }

    // ── OrDefault ────────────────────────────────────────────────────────────

    [Fact]
    public void OrDefault_Success_Returns_Value()
    {
        Assert.Equal("actual", HttpResult<string>.Ok("actual").OrDefault("fallback"));
    }

    [Fact]
    public void OrDefault_Transient_Returns_Fallback()
    {
        Assert.Equal("fallback", HttpResult<string>.Transient("").OrDefault("fallback"));
    }

    [Fact]
    public void OrDefault_Definitive_Returns_Fallback()
    {
        Assert.Equal("fallback", HttpResult<string>.Definitive(404, "").OrDefault("fallback"));
    }

    // ── Payload integrity ────────────────────────────────────────────────────

    [Fact]
    public void Success_Preserves_Reference_Identity()
    {
        var list = new List<string> { "a", "b" };
        var s = Assert.IsType<HttpResult<List<string>>.Success>(HttpResult<List<string>>.Ok(list));
        Assert.Same(list, s.Value);
    }

    [Fact]
    public void TransientFailure_Carries_Exception()
    {
        var ex = new TimeoutException("timed out");
        var t = Assert.IsType<HttpResult<int>.TransientFailure>(HttpResult<int>.Transient("timeout", ex));
        Assert.Same(ex, t.Exception);
    }

    [Fact]
    public void TransientFailure_Exception_Can_Be_Null()
    {
        var t = Assert.IsType<HttpResult<int>.TransientFailure>(HttpResult<int>.Transient("network down"));
        Assert.Null(t.Exception);
    }

    [Fact]
    public void DefinitiveFailure_Null_StatusCode_Is_Valid()
    {
        // Used for JSON deserialization failures where there is no HTTP status code.
        var d = Assert.IsType<HttpResult<int>.DefinitiveFailure>(
            HttpResult<int>.Definitive(null, "deser failed"));
        Assert.Null(d.StatusCode);
        Assert.False(d.IsNotFound);
    }

    // ── Record equality (structural) ─────────────────────────────────────────

    [Fact]
    public void Success_Records_With_Equal_Values_Are_Equal()
    {
        var a = HttpResult<int>.Ok(7);
        var b = HttpResult<int>.Ok(7);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Success_Records_With_Different_Values_Are_Not_Equal()
    {
        Assert.NotEqual(HttpResult<int>.Ok(1), HttpResult<int>.Ok(2));
    }

    [Fact]
    public void TransientFailure_Records_With_Same_Reason_Are_Equal()
    {
        var a = HttpResult<int>.Transient("err");
        var b = HttpResult<int>.Transient("err");
        Assert.Equal(a, b);
    }
}
