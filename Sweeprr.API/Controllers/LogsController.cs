using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sweeprr.API.Controllers;

[Authorize]
[ApiController]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private static readonly Regex LogLineRegex = new(
        @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[([A-Z]{3})\] \(([^)]+)\) (.*)$",
        RegexOptions.Compiled);

    public LogsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Retrieves a paginated list of log entries, optionally filtered.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var configDir = _configuration["ConfigDir"] ?? "/config";
        var logsDir = Path.Combine(configDir, "logs");

        if (!Directory.Exists(logsDir))
        {
            return Ok(new { items = Array.Empty<LogEntryDto>(), totalCount = 0, page, pageSize });
        }

        var files = Directory.GetFiles(logsDir, "sweeprr-*.log")
            .OrderByDescending(f => f)
            .ToList();

        var allMatching = new List<LogEntry>();

        foreach (var file in files.Take(5)) // Limit to last 5 log files for performance
        {
            var entries = await ParseLogFileAsync(file, ct);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (MatchesFilter(entry, level, category, search))
                {
                    allMatching.Add(entry);
                    if (allMatching.Count >= 10000) // Upper limit safety cap
                    {
                        break;
                    }
                }
            }

            if (allMatching.Count >= 10000)
            {
                break;
            }
        }

        var totalCount = allMatching.Count;
        var items = allMatching
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new LogEntryDto(
                e.Timestamp,
                e.Level,
                e.Category,
                e.SourceContext,
                e.Message,
                e.Exception))
            .ToList();

        return Ok(new { items, totalCount, page, pageSize });
    }

    /// <summary>
    /// Downloads the latest raw log file.
    /// </summary>
    [HttpGet("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DownloadLogs()
    {
        var configDir = _configuration["ConfigDir"] ?? "/config";
        var logsDir = Path.Combine(configDir, "logs");

        if (!Directory.Exists(logsDir))
        {
            return NotFound("Logs directory not found.");
        }

        var files = Directory.GetFiles(logsDir, "sweeprr-*.log")
            .OrderByDescending(f => f)
            .ToList();

        if (files.Count == 0)
        {
            return NotFound("No log files found.");
        }

        var latestFile = files[0];
        var fileName = Path.GetFileName(latestFile);

        try
        {
            var stream = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return File(stream, "text/plain", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to download log file: {ex.Message}");
        }
    }

    // ── Parser and Helpers ───────────────────────────────────────────────────

    private async Task<List<LogEntry>> ParseLogFileAsync(string filePath, CancellationToken ct)
    {
        var entries = new List<LogEntry>();
        LogEntry? current = null;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            var match = LogLineRegex.Match(line);
            if (match.Success)
            {
                if (current != null)
                {
                    entries.Add(current);
                }

                var timestamp = DateTime.Parse(match.Groups[1].Value);
                var rawLevel = match.Groups[2].Value;
                var sourceContext = match.Groups[3].Value;
                var message = match.Groups[4].Value;

                var level = rawLevel switch
                {
                    "DBG" => "Debug",
                    "INF" => "Information",
                    "WRN" => "Warning",
                    "ERR" => "Error",
                    "FTL" => "Fatal",
                    _ => rawLevel
                };

                var category = MapCategory(sourceContext);

                current = new LogEntry(timestamp, level, category, sourceContext, message, null);
            }
            else if (current != null)
            {
                if (current.Exception == null)
                {
                    current = current with { Exception = line };
                }
                else
                {
                    current = current with { Exception = current.Exception + "\n" + line };
                }
            }
        }

        if (current != null)
        {
            entries.Add(current);
        }

        return entries;
    }

    public static string MapCategory(string sourceContext)
    {
        if (string.IsNullOrEmpty(sourceContext)) return "System";

        if (sourceContext.Contains("SweepExecutor") ||
            sourceContext.Contains("ScanPipeline") ||
            sourceContext.Contains("SchedulerHostedService") ||
            sourceContext.Contains("WatchAggregationService") ||
            sourceContext.Contains("MediaPopulationService") ||
            sourceContext.Contains("FailsafeService"))
        {
            return "Sweep";
        }

        if (sourceContext.Contains("ConnectionService") ||
            sourceContext.Contains("ConnectionTestService") ||
            sourceContext.Contains("Integrations") ||
            sourceContext.Contains("Client") ||
            sourceContext.Contains("WebSocket"))
        {
            return "Connection";
        }

        if (sourceContext.Contains("Rules") ||
            sourceContext.Contains("RuleEvaluator") ||
            sourceContext.Contains("RuleGroups"))
        {
            return "Rule";
        }

        if (sourceContext.Contains("AuthService") ||
            sourceContext.Contains("AuthController"))
        {
            return "Auth";
        }

        return "System";
    }

    private static bool MatchesFilter(LogEntry entry, string? level, string? category, string? search)
    {
        if (!string.IsNullOrEmpty(level) && !entry.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(category) && !entry.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(search))
        {
            bool inMessage = entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase);
            bool inContext = entry.SourceContext.Contains(search, StringComparison.OrdinalIgnoreCase);
            bool inException = entry.Exception != null && entry.Exception.Contains(search, StringComparison.OrdinalIgnoreCase);

            if (!inMessage && !inContext && !inException)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record LogEntry(
    DateTime Timestamp,
    string Level,
    string Category,
    string SourceContext,
    string Message,
    string? Exception
);

public sealed record LogEntryDto(
    DateTime Timestamp,
    string Level,
    string Category,
    string SourceContext,
    string Message,
    string? Exception
);
