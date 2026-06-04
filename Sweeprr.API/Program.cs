using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Sweeprr.API.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

builder.Services.AddSweeprrDataProtection(builder.Configuration);
builder.Services.AddSweeprrDatabase(builder.Configuration);
builder.Services.AddSweeprrAuth();

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
