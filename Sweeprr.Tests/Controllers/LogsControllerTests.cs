using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Sweeprr.API.Controllers;

namespace Sweeprr.Tests.Controllers;

public class LogsControllerTests : IDisposable
{
    private readonly string _tempConfigDir;
    private readonly string _logsDir;
    private readonly IConfiguration _config;
    private readonly LogsController _controller;

    public LogsControllerTests()
    {
        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"sweeprr_tests_logs_{Guid.NewGuid()}");
        _logsDir = Path.Combine(_tempConfigDir, "logs");
        Directory.CreateDirectory(_logsDir);

        var settings = new Dictionary<string, string?>
        {
            ["ConfigDir"] = _tempConfigDir
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        _controller = new LogsController(_config);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempConfigDir))
            {
                Directory.Delete(_tempConfigDir, true);
            }
        }
        catch { /* ignore */ }
    }

    [Theory]
    [InlineData("Sweeprr.API.Services.SweepExecutor", "Sweep")]
    [InlineData("Sweeprr.API.Background.ScanPipeline", "Sweep")]
    [InlineData("Sweeprr.API.Background.SchedulerHostedService", "Sweep")]
    [InlineData("Sweeprr.API.Services.ConnectionService", "Connection")]
    [InlineData("Sweeprr.API.Services.ConnectionTestService", "Connection")]
    [InlineData("Sweeprr.API.Integrations.Jellyfin.JellyfinClient", "Connection")]
    [InlineData("Sweeprr.API.Services.Rules.RuleEvaluator", "Rule")]
    [InlineData("Sweeprr.API.Controllers.RuleGroupsController", "Rule")]
    [InlineData("Sweeprr.API.Services.AuthService", "Auth")]
    [InlineData("Sweeprr.API.Controllers.AuthController", "Auth")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", "System")]
    [InlineData("System.Net.Http.HttpClient", "Connection")]
    public void MapCategory_Returns_Correct_Category_For_Context(string context, string expectedCategory)
    {
        var result = LogsController.MapCategory(context);
        Assert.Equal(expectedCategory, result);
    }

    [Fact]
    public async Task GetLogs_Returns_Empty_When_No_Logs_Exist()
    {
        var result = await _controller.GetLogs(null, null, null);
        var okResult = Assert.IsType<OkObjectResult>(result);

        // Verify the structure
        dynamic data = okResult.Value!;
        Assert.Empty(data.items);
        Assert.Equal(0, data.totalCount);
        Assert.Equal(1, data.page);
        Assert.Equal(50, data.pageSize);
    }

    [Fact]
    public async Task GetLogs_Parses_Single_And_MultiLine_LogEntries()
    {
        var logFile = Path.Combine(_logsDir, "sweeprr-20260606.log");
        var logContent = 
            "2026-06-06 10:00:00.123 +05:30 [INF] (Sweeprr.API.Services.SweepExecutor) Sweep started {}\n" +
            "2026-06-06 10:00:01.456 +05:30 [WRN] (Sweeprr.API.Services.ConnectionService) Connection warning {}\n" +
            "2026-06-06 10:00:02.789 +05:30 [ERR] (Sweeprr.API.Services.AuthService) Auth failed {}\n" +
            "System.Exception: Unauthorized access\n" +
            "   at Sweeprr.API.Services.AuthService.Verify() in AuthService.cs:line 42\n";

        await File.WriteAllTextAsync(logFile, logContent);

        var result = await _controller.GetLogs(null, null, null, 1, 10);
        var okResult = Assert.IsType<OkObjectResult>(result);
        
        dynamic data = okResult.Value!;
        var items = (List<LogEntryDto>)data.items;

        Assert.Equal(3, data.totalCount);
        Assert.Equal(3, items.Count);

        // Newest should be first (reversed order)
        Assert.Equal("Error", items[0].Level);
        Assert.Equal("Auth", items[0].Category);
        Assert.Equal("Auth failed {}", items[0].Message);
        Assert.NotNull(items[0].Exception);
        Assert.Contains("System.Exception: Unauthorized access", items[0].Exception);

        Assert.Equal("Warning", items[1].Level);
        Assert.Equal("Connection", items[1].Category);
        Assert.Equal("Connection warning {}", items[1].Message);
        Assert.Null(items[1].Exception);

        Assert.Equal("Information", items[2].Level);
        Assert.Equal("Sweep", items[2].Category);
        Assert.Equal("Sweep started {}", items[2].Message);
    }

    [Fact]
    public async Task GetLogs_Filters_By_Level_Category_And_SearchQuery()
    {
        var logFile = Path.Combine(_logsDir, "sweeprr-20260606.log");
        var logContent = 
            "2026-06-06 10:00:00.123 +05:30 [INF] (Sweeprr.API.Services.SweepExecutor) Sweep started {}\n" +
            "2026-06-06 10:00:01.456 +05:30 [WRN] (Sweeprr.API.Services.ConnectionService) Connection warning {}\n" +
            "2026-06-06 10:00:02.789 +05:30 [ERR] (Sweeprr.API.Services.AuthService) Auth failed {}\n";

        await File.WriteAllTextAsync(logFile, logContent);

        // Filter by Level
        var result = await _controller.GetLogs("Warning", null, null);
        dynamic data = Assert.IsType<OkObjectResult>(result).Value!;
        var items = (List<LogEntryDto>)data.items;
        Assert.Single(items);
        Assert.Equal("Warning", items[0].Level);

        // Filter by Category
        result = await _controller.GetLogs(null, "Auth", null);
        data = Assert.IsType<OkObjectResult>(result).Value!;
        items = (List<LogEntryDto>)data.items;
        Assert.Single(items);
        Assert.Equal("Auth", items[0].Category);

        // Search text
        result = await _controller.GetLogs(null, null, "started");
        data = Assert.IsType<OkObjectResult>(result).Value!;
        items = (List<LogEntryDto>)data.items;
        Assert.Single(items);
        Assert.Equal("Sweep started {}", items[0].Message);
    }

    [Fact]
    public async Task GetLogs_Paginates_Correctly()
    {
        var logFile = Path.Combine(_logsDir, "sweeprr-20260606.log");
        var logContent = "";
        for (int i = 0; i < 15; i++)
        {
            logContent += $"2026-06-06 10:00:{i:D2}.000 +05:30 [INF] (Sweeprr.API.Services.SweepExecutor) Message {i} {{}}\n";
        }

        await File.WriteAllTextAsync(logFile, logContent);

        // Page 1, Size 10
        var result = await _controller.GetLogs(null, null, null, 1, 10);
        dynamic data = Assert.IsType<OkObjectResult>(result).Value!;
        var items = (List<LogEntryDto>)data.items;
        Assert.Equal(15, data.totalCount);
        Assert.Equal(10, items.Count);
        Assert.Equal("Message 14 {}", items[0].Message); // Newest first

        // Page 2, Size 10
        result = await _controller.GetLogs(null, null, null, 2, 10);
        data = Assert.IsType<OkObjectResult>(result).Value!;
        items = (List<LogEntryDto>)data.items;
        Assert.Equal(5, items.Count);
        Assert.Equal("Message 4 {}", items[0].Message);
    }

    [Fact]
    public void DownloadLogs_Returns_NotFound_If_No_Log_Files()
    {
        var result = _controller.DownloadLogs();
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DownloadLogs_Returns_FileStreamResult_Of_Latest_Log()
    {
        var logFileOld = Path.Combine(_logsDir, "sweeprr-20260605.log");
        var logFileNew = Path.Combine(_logsDir, "sweeprr-20260606.log");

        await File.WriteAllTextAsync(logFileOld, "old logs");
        await File.WriteAllTextAsync(logFileNew, "new logs");

        var result = _controller.DownloadLogs();
        var fileResult = Assert.IsType<FileStreamResult>(result);

        Assert.Equal("text/plain", fileResult.ContentType);
        Assert.Equal("sweeprr-20260606.log", fileResult.FileDownloadName);
    }
}
