using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NeSud.Core;
using NeSud.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 8 * 1024 * 1024);
builder.Services.AddSingleton(new ServerStore(builder.Configuration["Database"] ??
    Path.Combine(builder.Environment.ContentRootPath, "data", "server.db")));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapAccountEndpoints();
app.MapRewardEndpoints();
app.MapPlayerEndpoints();
app.MapGameEndpoints();
app.Run();
