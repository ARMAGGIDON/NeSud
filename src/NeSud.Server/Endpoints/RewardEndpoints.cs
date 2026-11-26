using Microsoft.AspNetCore.SignalR;
using NeSud.Core;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapRewardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/daily", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.Daily(name)) : Results.Unauthorized());
        app.MapPost("/api/daily/choose", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, DailyChoice choice) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (s.Choose(name, choice) is not { } result)
                return Results.Conflict(new { error = "Цю картку вже вибрано або винагорода недоступна." });
            await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            return Results.Ok(result);
        });
        app.MapPost("/api/daily/spend", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, WalletRequest request) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (s.SpendDesktop(name, request) is not { } result)
                return Results.Conflict(new { error = "Недостатньо ресурсів або операція недоступна." });
            await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            return Results.Ok(result);
        });
    }
}
