using Microsoft.AspNetCore.SignalR;
using NeSud.Core;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapPlayerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/history", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.History(name)) : Results.Unauthorized());
        app.MapGet("/api/comparison", (ServerStore s, Difficulty difficulty, int score, int seconds, int? rules) =>
            Enum.IsDefined(difficulty) && score >= 0 && seconds >= 0 && (rules ?? Game.CurrentRulesVersion) is >= 0 and <= Game.CurrentRulesVersion ? Results.Ok(s.Compare(difficulty, score, seconds, rules ?? Game.CurrentRulesVersion)) : Results.BadRequest());
        app.MapGet("/api/history/changes", (HttpContext c, ServerStore s, long? after) => User(c, s) is { } name ? Results.Ok(s.HistorySince(name, Math.Max(0, after ?? 0))) : Results.Unauthorized());
        app.MapGet("/api/challenge/calendar", (HttpContext c, ServerStore s, string? month) => User(c, s) is { } name ? (s.Calendar(name, month) is { } calendar ? Results.Ok(calendar) : Results.BadRequest()) : Results.Unauthorized());
        app.MapGet("/api/profile", (HttpContext c, ServerStore s, string? user, int? page, Difficulty? difficulty) =>
        {
            if (difficulty is { } level && !Enum.IsDefined(level)) return Results.BadRequest();
            string? name = user is null ? User(c, s) : s.ResolvePublicName(user);
            return name is null ? Results.NotFound() : Results.Ok(s.Profile(name, Math.Clamp(page ?? 0, 0, 10000), difficulty));
        });
        app.MapGet("/api/profile/own", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.OwnDetails(name)) : Results.Unauthorized());
        app.MapGet("/api/players", (HttpContext c, ServerStore s, string? q) =>
            User(c, s) is not { } name ? Results.Unauthorized() :
            !s.IsVerified(name) ? VerificationRequired() : Results.Ok(s.FindPlayers(q ?? "")));
        app.MapGet("/api/friends", (HttpContext c, ServerStore s) =>
            User(c, s) is not { } name ? Results.Unauthorized() :
            !s.IsVerified(name) ? VerificationRequired() : Results.Ok(s.Friends(name)));
        app.MapPost("/api/friends", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, FriendRequest request) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (!s.IsVerified(name)) return VerificationRequired();
            if (!s.ChangeFriend(name, request)) return Results.Conflict(new { error = "Дія недоступна." });
            await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            if (s.ResolvePublicName(request.User) is { } other)
                await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(other)));
            return Results.Ok(new SyncAck(true));
        });
        app.MapGet("/api/weekly", (ServerStore s) => s.Weekly());
        app.MapGet("/api/accumulated", (ServerStore s, string? metric, bool? all, Difficulty? difficulty) => difficulty is { } level && !Enum.IsDefined(level) ? Results.BadRequest() : Results.Ok(s.Accumulated(metric ?? "score", all ?? false, difficulty)));
        app.MapGet("/api/ranking", (ServerStore s, string? mode) => s.Rankings(mode == "all"));
        app.MapPost("/api/sync", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, List<GameRecord> records) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (records.Count > 20 || records.Any(r => r is null || r.Source != "desktop" || r.ChallengeDay is not null || !r.Valid()))
                return Results.BadRequest(new { error = "Некоректний запис історії." });
            s.Upload(name, records);
            if (records.Count > 0)
            {
                await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
                await GameHub.NotifyRankings(hub.Clients.Group(GameHub.RankingsGroup));
            }
            return Results.Ok(s.Acknowledge(name, records));
        });

        app.MapPost("/api/desktop/draft", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, DesktopDraft draft) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (draft.Id == Guid.Empty || draft.Started > draft.Updated || draft.Updated > DateTimeOffset.UtcNow.AddMinutes(5)
                || draft.Game is null || !draft.Game.Valid() || draft.Game.Completed) return Results.BadRequest();
            bool saved = s.SaveDraft(name, draft);
            if (saved) await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            return Results.Ok(new SyncAck(saved));
        });
        app.MapGet("/api/desktop/drafts", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.Drafts(name)) : Results.Unauthorized());
    }
}
