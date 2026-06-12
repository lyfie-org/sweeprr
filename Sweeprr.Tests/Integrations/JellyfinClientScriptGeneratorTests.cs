using Sweeprr.API.Integrations.Jellyfin;

namespace Sweeprr.Tests.Integrations;

public class JellyfinClientScriptGeneratorTests
{
    [Fact]
    public void Generate_EmbedsBaseUrl_AsJsonString()
    {
        var script = JellyfinClientScriptGenerator.Generate("https://sweeprr.example.com");

        Assert.Contains("const SWEEPRR_BASE = \"https://sweeprr.example.com\";", script);
    }

    [Fact]
    public void Generate_TrimsTrailingSlash_FromBaseUrl()
    {
        var script = JellyfinClientScriptGenerator.Generate("https://sweeprr.example.com/");

        Assert.Contains("const SWEEPRR_BASE = \"https://sweeprr.example.com\";", script);
        Assert.DoesNotContain("com/\";", script);
    }

    [Fact]
    public void Generate_ReferencesMediaStatusAndExtendEndpoints()
    {
        var script = JellyfinClientScriptGenerator.Generate("https://sweeprr.example.com");

        Assert.Contains("/api/public/media/${itemId}/status", script);
        Assert.Contains("${SWEEPRR_BASE}/extend?itemId=${encodeURIComponent(itemId)}", script);
    }

    [Fact]
    public void Generate_ExtractItemId_ParsesHashRouteBeforeSearch()
    {
        var script = JellyfinClientScriptGenerator.Generate("https://sweeprr.example.com");

        Assert.Contains("window.location.hash.split('?')[1]", script);
        Assert.Contains("window.location.search", script);
    }

    [Fact]
    public void Generate_InjectBanner_UsesItemIdParameter_NotStatusItemId()
    {
        var script = JellyfinClientScriptGenerator.Generate("https://sweeprr.example.com");

        Assert.Contains("function injectBanner(itemId, status)", script);
        Assert.DoesNotContain("status.itemId", script);
    }
}
