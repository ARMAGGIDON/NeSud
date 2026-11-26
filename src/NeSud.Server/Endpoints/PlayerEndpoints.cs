using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NeSud.Core;
using NeSud.Server;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapPlayerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/history", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.History(name)) : Results.Unauthorized());
        app.MapGet("/api/comparison", (ServerStore s, Difficulty difficulty, int score, int seconds, int? rules) =>
            Enum.IsDefined(difficulty) && score >= 0 && seconds >= 0 && (rules ?? 3) is >= 0 and <= 3 ? Results.Ok(s.Compare(difficulty, score, seconds, rules ?? 3)) : Results.BadRequest());
        app.MapGet("/api/history/changes", (HttpContext c, ServerStore s, long? after) => User(c,s) is { } name ? Results.Ok(s.HistorySince(name,Math.Max(0,after ?? 0))) : Results.Unauthorized());
        app.MapGet("/api/challenge/calendar", (HttpContext c, ServerStore s, string? month) => User(c,s) is { } name ? (s.Calendar(name,month) is { } calendar ? Results.Ok(calendar) : Results.BadRequest()) : Results.Unauthorized());
        app.MapGet("/api/profile", (HttpContext c, ServerStore s, string? user, int? page, Difficulty? difficulty) =>
        {
            if(difficulty is { } level && !Enum.IsDefined(level)) return Results.BadRequest();
            string? name = user is null ? User(c,s) : s.ResolvePublicName(user);
            return name is null ? Results.NotFound() : Results.Ok(s.Profile(name,Math.Clamp(page ?? 0,0,10000),difficulty));
        });
        app.MapGet("/api/players", (ServerStore s,string? q) => s.FindPlayers(q ?? ""));
        app.MapGet("/api/friends", (HttpContext c,ServerStore s) => User(c,s) is { } name ? Results.Ok(s.Friends(name)) : Results.Unauthorized());
        app.MapPost("/api/friends", (HttpContext c,ServerStore s,FriendRequest request) =>
            User(c,s) is not { } name ? Results.Unauthorized() : s.ChangeFriend(name,request) ? Results.Ok(new SyncAck(true)) : Results.Conflict(new { error="Дія недоступна." }));
        app.MapGet("/api/weekly", (ServerStore s) => s.Weekly());
        app.MapGet("/api/accumulated", (ServerStore s,string? metric,bool? all,Difficulty? difficulty) => difficulty is { } level && !Enum.IsDefined(level) ? Results.BadRequest() : Results.Ok(s.Accumulated(metric ?? "score",all ?? false,difficulty)));
        app.MapGet("/api/ranking", (ServerStore s, string? mode) => s.Rankings(mode == "all"));
        app.MapPost("/api/sync", (HttpContext c, ServerStore s, List<GameRecord> records) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (records.Count > 20 || records.Any(r => r is null || r.Source != "desktop" || !r.Valid()))
                return Results.BadRequest(new { error = "Некоректний запис історії." });
            s.Upload(name, records); return Results.Ok(s.Acknowledge(name, records));
        });

        app.MapPost("/api/desktop/draft", (HttpContext c, ServerStore s, DesktopDraft draft) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (draft.Id == Guid.Empty || draft.Started > draft.Updated || draft.Updated > DateTimeOffset.UtcNow.AddMinutes(5)
                || draft.Game is null || !draft.Game.Valid() || draft.Game.Completed) return Results.BadRequest();
            return Results.Ok(new SyncAck(s.SaveDraft(name, draft)));
        });
        app.MapGet("/api/desktop/drafts", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.Drafts(name)) : Results.Unauthorized());
    }
}
