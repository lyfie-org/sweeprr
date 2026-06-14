using System.Threading.Channels;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Scalar.AspNetCore;
using Sweeprr.API.Background;
using Sweeprr.API.Configuration;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

var configDir = builder.Configuration["ConfigDir"] ?? "/config";
Directory.CreateDirectory(Path.Combine(configDir, "logs"));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(configDir, "logs", "sweeprr-.log"),
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj} {Properties:j}{NewLine}{Exception}"
    )
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();
        document.Components.SecuritySchemes.Add("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Name = "Authorization",
            Description = "Enter JWT Bearer token."
        });

        var endpointSummaries = new Dictionary<(string Method, string Path), string>()
        {
            { ("POST", "/api/auth/setup"), "Create the initial admin account (first-run only)" },
            { ("POST", "/api/auth/login"), "Authenticate and receive a JWT access token (rate-limited)" },
            { ("GET", "/api/auth/me"), "Retrieve the profile of the currently authenticated user" },
            { ("GET", "/api/auth/status"), "Check whether this is a first-run instance" },
            { ("GET", "/api/apikeys"), "List all generated API keys" },
            { ("POST", "/api/apikeys"), "Generate a new API key" },
            { ("DELETE", "/api/apikeys/{id}"), "Revoke/delete an API key" },
            { ("GET", "/api/sweep"), "Query and paginate the active sweep queue" },
            { ("GET", "/api/sweep/summary"), "Retrieve sweep item counts categorized by status" },
            { ("GET", "/api/sweep/{id}"), "Retrieve a specific sweep item by its ID" },
            { ("POST", "/api/sweep/{id}/approve"), "Approve a flagged item for deletion" },
            { ("POST", "/api/sweep/{id}/ignore"), "Ignore an item (optionally creating a permanent exclusion)" },
            { ("POST", "/api/sweep/{id}/skip"), "Temporarily skip an item" },
            { ("POST", "/api/sweep/execute"), "Execute physical file deletions on all approved queue items" },
            { ("POST", "/api/sweep/run"), "Manually trigger rule evaluation to populate the sweep queue" },
            { ("GET", "/api/rulegroups"), "Retrieve all configured rule groups" },
            { ("GET", "/api/rulegroups/{id}"), "Retrieve a specific rule group by ID" },
            { ("GET", "/api/rulegroups/fields"), "Fetch available rule fields" },
            { ("GET", "/api/rulegroups/tags"), "Proxy endpoint to fetch tags from Radarr/Sonarr for rule definitions" },
            { ("POST", "/api/rulegroups"), "Create a new rule group" },
            { ("PUT", "/api/rulegroups/{id}"), "Update rule group configuration/rules" },
            { ("DELETE", "/api/rulegroups/{id}"), "Delete a rule group" },
            { ("POST", "/api/rulegroups/test-rule"), "Test-evaluate a single rule on mock context" },
            { ("POST", "/api/rulegroups/validate"), "Validate a rule group configuration for syntax errors" },
            { ("GET", "/api/exclusions"), "Retrieve all media item exclusions (global or rule-scoped)" },
            { ("DELETE", "/api/exclusions/{id}"), "Delete a media item exclusion" },
            { ("GET", "/api/exclusions/tags"), "Retrieve all tag-based exclusions" },
            { ("POST", "/api/exclusions/tags"), "Create a new tag exclusion" },
            { ("DELETE", "/api/exclusions/tags/{id}"), "Delete a tag exclusion" },
            { ("GET", "/api/media"), "Query, search, and paginate indexed media items" },
            { ("GET", "/api/media/{id}/ruletrace"), "Retrieve a detailed audit trace explaining why rules matched or skipped a media item" },
            { ("POST", "/api/media/queue-manual"), "Manually force-flag a media item into the sweep queue" },
            { ("POST", "/api/media/exclude-bulk"), "Create bulk exclusions for multiple media items" },
            { ("GET", "/api/playback/activity/{mediaServerItemId}"), "Fetch playback activity history for a specific media item" },
            { ("GET", "/api/connections"), "Retrieve all external server connections" },
            { ("GET", "/api/connections/{id}"), "Get connection details" },
            { ("POST", "/api/connections"), "Create a new server connection" },
            { ("PUT", "/api/connections/{id}"), "Update connection details" },
            { ("DELETE", "/api/connections/{id}"), "Delete a server connection" },
            { ("POST", "/api/connections/{id}/test"), "Test an existing server connection" },
            { ("POST", "/api/connections/test-unsaved"), "Test connection settings before saving them" },
            { ("GET", "/api/connections/{id}/qualityprofiles"), "Fetch quality profiles from Radarr/Sonarr" },
            { ("GET", "/api/connections/{id}/tags"), "Fetch tags from Radarr/Sonarr" },
            { ("GET", "/api/connections/{id}/diskspace"), "Fetch root paths and available disk space" },
            { ("GET", "/api/settings"), "Retrieve global application settings" },
            { ("PATCH", "/api/settings"), "Update global application settings" },
            { ("GET", "/api/settings/backup"), "Get automated backup configuration" },
            { ("PUT", "/api/settings/backup"), "Update backup configurations" },
            { ("POST", "/api/settings/backup/trigger"), "Manually trigger a SQLite database backup" },
            { ("GET", "/api/settings/backup/history"), "Retrieve past backup runs" },
            { ("GET", "/api/settings/notification"), "Get webhook/notification destinations" },
            { ("POST", "/api/settings/notification"), "Create a new notification destination" },
            { ("PUT", "/api/settings/notification/{id}"), "Update a notification destination" },
            { ("DELETE", "/api/settings/notification/{id}"), "Delete a notification destination" },
            { ("POST", "/api/settings/notification/test"), "Test an unsaved notification configuration" },
            { ("POST", "/api/settings/notification/{id}/test"), "Test an existing notification destination" },
            { ("GET", "/api/dashboard/stats"), "Retrieve top-level KPIs" },
            { ("GET", "/api/dashboard/activity"), "Fetch a log of recent curation activities" },
            { ("GET", "/api/dashboard/sparkline"), "Get data points representing historical curation trends" },
            { ("GET", "/api/system/info"), "Retrieve app version and build/release metadata" },
            { ("GET", "/api/logs"), "Query and view server logs" },
            { ("GET", "/api/logs/download"), "Download raw log files" },
            { ("GET", "/api/health"), "Return simple health check status" },
            { ("GET", "/api/jellyfin/status"), "Realtime WebSocket status representing session connections" },
            { ("GET", "/api/jellyfin/client-script.js"), "Serving the in-UI Jellyfin integrations portal scripts" },
            { ("POST", "/api/public/auth/jellyfin"), "Authenticate Jellyfin sessions for the in-UI script" },
            { ("POST", "/api/public/extend"), "Process extension portal requests" },
            { ("GET", "/api/public/media/{jellyfinItemId}/status"), "Fetch status indicators visible in Jellyfin UI overlays" }
        };

        foreach (var path in document.Paths)
        {
            var route = path.Key;
            var normalizedRoute = route.StartsWith("/") ? route : "/" + route;

            foreach (var operation in path.Value.Operations)
            {
                var method = operation.Key;
                var opValue = operation.Value;

                opValue.Security.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Id = "Bearer",
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme
                        }
                    }] = Array.Empty<string>()
                });

                // Override Summary if it exists in our exact lookup dictionary
                var lookupKey = (method.ToString().ToUpperInvariant(), normalizedRoute.ToLowerInvariant());
                if (endpointSummaries.TryGetValue(lookupKey, out var exactSummary))
                {
                    opValue.Summary = exactSummary;
                }
                else if (string.IsNullOrWhiteSpace(opValue.Summary))
                {
                    var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var verb = method switch
                    {
                        Microsoft.OpenApi.Models.OperationType.Get => "Retrieve",
                        Microsoft.OpenApi.Models.OperationType.Post => "Create or trigger",
                        Microsoft.OpenApi.Models.OperationType.Put => "Update",
                        Microsoft.OpenApi.Models.OperationType.Patch => "Partially update",
                        Microsoft.OpenApi.Models.OperationType.Delete => "Delete",
                        _ => method.ToString()
                    };

                    var entity = parts.Length > 1
                        ? string.Join(" ", parts.Skip(1).Select(p => p.StartsWith('{') ? "" : p)).Trim()
                        : (parts.Length > 0 ? parts[0] : "endpoint");

                    if (string.IsNullOrWhiteSpace(entity))
                    {
                        entity = parts.Length > 0 ? parts[0] : "endpoint";
                    }

                    if (entity.Length > 0)
                    {
                        entity = char.ToUpper(entity[0]) + entity.Substring(1);
                    }

                    opValue.Summary = $"{verb} {entity}";
                }

                if (string.IsNullOrWhiteSpace(opValue.Description))
                {
                    opValue.Description = opValue.Summary;
                }

                // Check for danger endpoints: any DELETE endpoint or POST /api/sweep/execute
                var isDanger = method == Microsoft.OpenApi.Models.OperationType.Delete ||
                               (method == Microsoft.OpenApi.Models.OperationType.Post && route.EndsWith("/execute", StringComparison.OrdinalIgnoreCase));

                if (isDanger)
                {
                    if (!opValue.Summary.StartsWith("⚠️ DANGER:", StringComparison.OrdinalIgnoreCase) &&
                        !opValue.Summary.StartsWith("🚨 DANGER:", StringComparison.OrdinalIgnoreCase))
                    {
                        opValue.Summary = $"🚨 DANGER: {opValue.Summary}";
                    }

                    var dangerWarning = "\n\n> [!CAUTION]\n> **🚨 DANGER:** This is a destructive or critical operation. Exercise caution when invoking this endpoint.";
                    if (!opValue.Description.Contains("DANGER:", StringComparison.OrdinalIgnoreCase))
                    {
                        opValue.Description += dangerWarning;
                    }
                }
            }
        }
        return Task.CompletedTask;
    });
});

builder.Services.AddSweeprrDataProtection(builder.Configuration);
builder.Services.AddSweeprrDatabase(builder.Configuration);
builder.Services.AddSweeprrAuth();

builder.Services.AddScoped<IOverlayRenderingService, OverlayRenderingService>();
builder.Services.AddScoped<ISweepQueueService, SweepQueueService>();
builder.Services.AddScoped<IMediaPopulationService, MediaPopulationService>();
builder.Services.AddScoped<IFailsafeService, FailsafeService>();
builder.Services.AddScoped<ISweepExecutor, SweepExecutor>();

builder.Services.AddScoped<IConnectionService, ConnectionService>();
builder.Services.AddScoped<IConnectionTestService, ConnectionTestService>();
builder.Services.AddScoped<IMediaMatchingService, MediaMatchingService>();
builder.Services.AddScoped<IRuleValidationService, RuleValidationService>();
builder.Services.AddScoped<IValueResolver, ValueResolver>();
builder.Services.AddScoped<IRuleEvaluator, RuleEvaluator>();
builder.Services.AddScoped<IWatchAggregationService, WatchAggregationService>();
builder.Services.AddScoped<IMediaExplorerService, MediaExplorerService>();

// In-app Jellyfin session alerts + pre-sweep broadcast warnings (Story 10.2).
// Singleton because it's invoked from other singletons (WS service, scheduler).
builder.Services.AddSingleton<IJellyfinSessionAlertService, JellyfinSessionAlertService>();

// Background scheduler — singleton so the controller can trigger manual scans on the same instance.
builder.Services.AddSingleton<IScanPipeline, ScanPipeline>();
builder.Services.AddSingleton<SchedulerHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchedulerHostedService>());

// Typed HTTP clients (Jellyfin, Radarr, Sonarr) with Polly resilience pipelines.
// Registers IIntegrationClientFactory + named HttpClient pools.
builder.Services.AddSweeprrHttpClients();

// Playstate cache — singleton so both the WS service and the rule engine (Sprint 3)
// share the same in-memory store without additional locking at the DI layer.
builder.Services.AddSingleton<IPlaystateCache, PlaystateCache>();
builder.Services.AddSingleton<IPlaybackActivityWriter, PlaybackActivityWriter>();
builder.Services.AddHostedService<PlaybackPruningWorker>();
builder.Services.AddHostedService<ExpiredExclusionCleanupWorker>();

// Jellyfin Playback Reporting plugin detection + backfill (Story 10.1).
builder.Services.AddScoped<IPlaybackReportingService, PlaybackReportingService>();
builder.Services.AddHostedService<PlaybackReportingBackfillWorker>();

// Notification pipeline (Story 11.1): sweep/scan/WS code paths enqueue onto this channel via
// INotificationService (non-blocking), NotificationDispatchWorker drains it and performs the
// actual webhook HTTP calls so delivery failures never affect the sweep/scan pipeline.
builder.Services.AddSingleton(Channel.CreateUnbounded<NotificationDispatchRequest>());
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<INotificationProvider, DiscordNotificationProvider>();
builder.Services.AddSingleton<INotificationProvider, GenericWebhookNotificationProvider>();
builder.Services.AddHostedService<NotificationDispatchWorker>();

// Automated backups (Story 11.3) — singleton scheduler so the controller can read
// GetNextScheduledRun() / trigger a reload on the same instance.
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddSingleton<BackupSchedulerHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupSchedulerHostedService>());

// Channel used to signal JellyfinCurationWarningSyncService when the sweep queue changes.
// Bounded(1) + DropOldest: multiple rapid writes collapse to a single sync run.
builder.Services.AddSingleton(Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
{
    FullMode     = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
    SingleWriter = false
}));
builder.Services.AddHostedService<JellyfinCurationWarningSyncService>();

// Jellyfin WebSocket service — register the concrete type as a singleton first so
// that AddHostedService and IJellyfinWebSocketStatus both resolve the same instance.
builder.Services.AddSingleton<JellyfinWebSocketService>();
builder.Services.AddSingleton<IJellyfinWebSocketStatus>(
    sp => sp.GetRequiredService<JellyfinWebSocketService>());
builder.Services.AddHostedService(
    sp => sp.GetRequiredService<JellyfinWebSocketService>());

// CORS policy for anonymous endpoints fetched cross-origin — the Jellyfin in-UI client
// script (Story 10.5) runs on Jellyfin's domain and calls /api/public/* on Sweeprr's.
builder.Services.AddCors(options =>
{
    options.AddPolicy("PublicApi", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Rate limiter: fixed window, 10 attempts / 60 s per IP — applied to POST /api/auth/login.
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 10;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference(options =>
{
    options.Title = "Sweeprr API";
    options.Favicon = "/sweeprr_logo.png";
    options.HeaderContent = "<div style='display:flex;align-items:center;'><img src='/sweeprr_logo.png' alt='Sweeprr' style='height:42px;margin-right:12px;' /><span style='font-size:22px;font-weight:700;color:#fff;font-family:system-ui, -apple-system, sans-serif;'>Sweeprr</span></div>";
    options.Theme = ScalarTheme.Purple;
    options.DarkMode = true;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    options.Authentication = new ScalarAuthenticationOptions
    {
        PreferredSecuritySchemes = ["Bearer"],
    };
    options.CustomCss = @"
        /* Hide developer tools, configure, share, and deploy buttons from Scalar header */
        .references-header-actions,
        .references-header-actions button,
        .scalar-client-header-actions,
        .scalar-client-header-actions button,
        .scalar-developer-tools,
        .show-developer-tools,
        .scalar-share-button,
        .scalar-deploy-button,
        .scalar-configure-button,
        .references-header-right,
        .references-header-right button,
        [class*='header-actions'],
        [class*='developer-tools'],
        [class*='share-button'],
        [class*='deploy-button'],
        [class*='configure-button'] {
            display: none !important;
        }
    ";
}).AllowAnonymous();

await app.Services.MigrateAndSeedAsync();
await app.Services.BackfillPlaystateCacheAsync();

app.UseRateLimiter();

app.UseDefaultFiles();

// index.html → no-cache; all other static assets → 1-year immutable (Vite hashes filenames)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "no-cache, no-store, must-revalidate";
            headers.Pragma = "no-cache";
        }
        else
        {
            headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
});

app.UseRouting();

app.UseCors("PublicApi");

// Step 1 — SPA-like catch-all for browser navigation (unmatched routes)
// Placed after UseRouting so GetEndpoint() works, but before UseAuthentication/UseAuthorization
// so unauthorized users don't get blocked with 401 when navigating to unmatched endpoints.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() == null)
    {
        var acceptHeader = context.Request.Headers.Accept.ToString();
        var isHtmlRequest = acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase);

        // For unmatched HTML requests (e.g. client-side page refreshes on React routes),
        // serve the SPA shell (index.html) so React Router can process it on the client,
        // unless it is a backend API path.
        if (isHtmlRequest && !context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(app.Environment.WebRootFileProvider.GetFileInfo("index.html"));
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Not found", docsUrl = "/scalar/v1" });
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// SPA fallback serves the index.html via the middleware catch-all above.
// /api/* routes take precedence over the SPA fallback
app.MapControllers();

try
{
    Log.Information("Starting web host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
