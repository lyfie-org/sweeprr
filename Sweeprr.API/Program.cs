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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.Services.MigrateAndSeedAsync();

app.UseDefaultFiles();

// Static files with cache strategy:
//   index.html     → no-cache (entry point references hashed assets)
//   everything else → 1-year immutable (Vite content-hashes filenames)
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

app.UseAuthorization();

// /api/* routes are registered here — they take precedence over the SPA fallback
app.MapControllers();

// Fallback: any unmatched route returns index.html so client-side routing works
app.MapFallbackToFile("index.html");

app.Run();
