using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using NeSud.Core;
using NeSud.Server;

var builder = WebApplication.CreateBuilder(args);
SmtpEnv.AddTo(builder.Configuration, builder.Environment.ContentRootPath);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 8 * 1024 * 1024);
builder.Services.AddSingleton(new ServerStore(builder.Configuration["Database"] ??
    Path.Combine(builder.Environment.ContentRootPath, "data", "server.db")));
builder.Services.AddSignalR(options =>
{
    options.SupportedProtocols = new List<string> { "messagepack" };
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.MaximumParallelInvocationsPerClient = 1;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
}).AddMessagePackProtocol();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
var app = builder.Build();
var stopping = app.Lifetime.ApplicationStopping;
app.Use(async (context, next) =>
{
    if (stopping.IsCancellationRequested)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    // Long-lived WebSocket requests must not hold Kestrel's graceful shutdown open.
    // Abort only the hub transport; ordinary requests can finish their transactions.
    if (context.Request.Path.StartsWithSegments("/hubs/game"))
    {
        using var registration = stopping.Register(static state => ((HttpContext)state!).Abort(), context);
        await next();
        return;
    }

    await next();
});
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob: data:; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapAccountEndpoints();
app.MapRewardEndpoints();
app.MapPlayerEndpoints();
app.MapGameEndpoints();
app.MapHub<GameHub>("/hubs/game", options =>
{
    options.Transports = HttpTransportType.WebSockets;
    options.ApplicationMaxBufferSize = 32 * 1024;
    options.TransportMaxBufferSize = 32 * 1024;
    options.WebSockets.CloseTimeout = TimeSpan.FromSeconds(2);
});
app.Run();
