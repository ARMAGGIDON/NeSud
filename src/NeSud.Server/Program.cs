using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using NeSud.Core;
using NeSud.Server;

var shutdownTrace = ShutdownTrace.Start();
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
shutdownTrace?.Observe(app.Lifetime);
var stopping = app.Lifetime.ApplicationStopping;
int activeHttp = 0, activeSockets = 0;
if (shutdownTrace is not null)
    stopping.Register(() => shutdownTrace.Write($"Shutdown.pending http={Volatile.Read(ref activeHttp)} websocket={Volatile.Read(ref activeSockets)}"));

app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (stopping.IsCancellationRequested)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    bool socket = context.Request.Path.StartsWithSegments("/hubs/game") &&
        context.WebSockets.IsWebSocketRequest;
    if (socket)
    {
        Interlocked.Increment(ref activeSockets);
        try
        {
            // The host must not wait for an idle persistent transport to drain.
            // Cancel only WebSockets; ordinary HTTP requests retain graceful shutdown.
            using var registration = stopping.Register(() =>
            {
                shutdownTrace?.Write("WebSocket.abort");
                context.Abort();
            });
            await next();
        }
        finally
        {
            Interlocked.Decrement(ref activeSockets);
            if (stopping.IsCancellationRequested) shutdownTrace?.Write("WebSocket.drained");
        }
    }
    else
    {
        Interlocked.Increment(ref activeHttp);
        try { await next(); }
        finally
        {
            Interlocked.Decrement(ref activeHttp);
            if (stopping.IsCancellationRequested) shutdownTrace?.Write("Http.drained");
        }
    }
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
try
{
    shutdownTrace?.Write("Run.enter");
    app.Run();
    shutdownTrace?.Write("Run.return");
}
catch (Exception ex)
{
    shutdownTrace?.Write("Run.error " + ex.GetType().FullName);
    throw;
}
