using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GridWatch.Api.Hubs;
using GridWatch.Api.Security;
using GridWatch.Infrastructure;
using GridWatch.Infrastructure.Data;
using GridWatch.Infrastructure.Ingestion;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGridWatchInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IGridNotifier, SignalRGridNotifier>();
builder.Services.AddHealthChecks().AddDbContextCheck<GridWatchDbContext>("database");

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
builder.Services.AddScoped<ApiKeyFilter>();

// Rate limits per client IP: generous for reads, tight for the admin endpoint.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimits.Read, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(RateLimits.Admin, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("GET", "POST").AllowCredentials()));

// Azure App Service terminates TLS in front of the app and forwards the original scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// TLS terminates at the platform (App Service with httpsOnly, or a reverse proxy), which also
// redirects HTTP to HTTPS. The app adds HSTS so browsers stay on HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();

// The React build is copied into wwwroot (npm run build, or the Docker image). In development
// the UI runs on the Vite dev server instead and wwwroot may not exist.
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var hasUi = File.Exists(Path.Combine(webRoot, "index.html"));
if (hasUi)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference("/docs", o => o.WithTitle("Ontario Grid Watch API"));
app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<GridHub>("/hubs/grid");
if (hasUi)
{
    // Client-side routes get the React app. "nonfile" leaves paths with a file extension (the JS and
    // CSS bundles) to the static file middleware, and unknown API and hub paths still return 404.
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|hubs/).*$)}", "index.html");
}

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
