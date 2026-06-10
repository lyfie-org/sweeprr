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

        foreach (var operation in document.Paths.Values.SelectMany(p => p.Operations))
        {
            operation.Value.Security.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
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
    options.HeaderContent = "<img src='/sweeprr_logo.png' alt='Sweeprr' style='height:28px;margin-right:10px;vertical-align:middle;' />";
    options.Theme = ScalarTheme.Purple;
    options.DarkMode = true;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    options.Authentication = new ScalarAuthenticationOptions
    {
        PreferredSecuritySchemes = ["Bearer"],
    };
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

// Step 1 — SPA-like catch-all for browser navigation (unmatched routes)
// Placed after UseRouting so GetEndpoint() works, but before UseAuthentication/UseAuthorization
// so unauthorized users don't get blocked with 401 when navigating to unmatched endpoints.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() == null)
    {
        var acceptHeader = context.Request.Headers.Accept.ToString();
        if (acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/scalar/v1");
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Not found", docsUrl = "/scalar/v1" });
        }
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// /api/* routes take precedence over the SPA fallback
app.MapControllers();

// Step 2 — Root redirect:
app.MapGet("/", () => Results.Redirect("/scalar/v1", permanent: false)).AllowAnonymous();

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
