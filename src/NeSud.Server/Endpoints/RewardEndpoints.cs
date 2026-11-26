using NeSud.Core;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapRewardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/daily", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.Daily(name)) : Results.Unauthorized());
        app.MapPost("/api/daily/choose", (HttpContext c, ServerStore s, DailyChoice choice) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            return s.Choose(name, choice) is { } result ? Results.Ok(result) : Results.Conflict(new { error = "Цю картку вже вибрано або винагорода недоступна." });
        });
        app.MapPost("/api/daily/spend", (HttpContext c, ServerStore s, WalletRequest request) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            return s.SpendDesktop(name, request) is { } result ? Results.Ok(result) : Results.Conflict(new { error = "Недостатньо ресурсів або операція недоступна." });
        });
    }
}
