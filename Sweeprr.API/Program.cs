using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Sweeprr.API.Configuration;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

builder.Services.AddSweeprrDataProtection(builder.Configuration);
builder.Services.AddSweeprrDatabase(builder.Configuration);
builder.Services.AddSweeprrAuth();

builder.Services.AddScoped<IConnectionService, ConnectionService>();
builder.Services.AddScoped<IConnectionTestService, ConnectionTestService>();
builder.Services.AddScoped<IMediaMatchingService, MediaMatchingService>();

// Typed HTTP clients (Jellyfin, Radarr, Sonarr) with Polly resilience pipelines.
// Registers IIntegrationClientFactory + named HttpClient pools.
builder.Services.AddSweeprrHttpClients();

// Playstate cache — singleton so both the WS service and the rule engine (Sprint 3)
// share the same in-memory store without additional locking at the DI layer.
builder.Services.AddSingleton<IPlaystateCache, PlaystateCache>();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.Services.MigrateAndSeedAsync();

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

app.UseAuthentication();
app.UseAuthorization();

// /api/* routes take precedence over the SPA fallback
app.MapControllers();

// Any unmatched route returns index.html for client-side routing
app.MapFallbackToFile("index.html");

app.Run();
