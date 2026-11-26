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
static string Token(HttpContext context)
{
    var value = context.Request.Headers.Authorization.ToString();
    return value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
}
static string? User(HttpContext context, ServerStore store) => store.Authenticate(Token(context));
static IResult Auth(Credentials c, ServerStore store, bool register)
{
    if (ServerStore.ValidateCredentials(c, register) is { } error) return Results.BadRequest(new { error });
    var session = store.Login(c, register);
    return session is not null ? Results.Ok(session) : register
        ? Results.Conflict(new { error = "Цей логін уже зайнятий." })
        : Results.Json(new { error = "Неправильний логін або пароль." }, statusCode: 401);
}
app.MapPost("/api/register", (Credentials c, ServerStore s) => Auth(c, s, true)).RequireRateLimiting("auth");
app.MapPost("/api/login", (Credentials c, ServerStore s) => Auth(c, s, false)).RequireRateLimiting("auth");
app.MapPost("/api/guest", (ServerStore s) => s.Guest() is { } guest
    ? Results.Ok(guest) : Results.Problem("Демонстраційні сеанси зайняті. Спробуйте пізніше.", statusCode: 503)).RequireRateLimiting("auth");
app.MapGet("/api/health", () => Results.Ok(new SyncAck(true)));
app.MapPost("/api/logout", (HttpContext c, ServerStore s) => { s.Logout(Token(c)); return Results.Ok(); });
app.MapGet("/api/history", (HttpContext c, ServerStore s) =>
    User(c, s) is { } name ? Results.Ok(s.History(name)) : Results.Unauthorized());
app.MapGet("/api/ranking", (ServerStore s, string? mode) => s.Rankings(mode == "all"));
app.MapPost("/api/sync", (HttpContext c, ServerStore s, List<GameRecord> records) =>
{
    if (User(c, s) is not { } name) return Results.Unauthorized();
    if (records.Count > 20 || records.Any(r => r is null || r.Source != "desktop" || !r.Valid()))
        return Results.BadRequest(new { error = "Некоректний запис історії." });
    s.Upload(name, records); return Results.Ok(s.History(name));
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

static WebGame Tick(WebGame session)
{
    var now = DateTimeOffset.UtcNow;
    var elapsed = (now - session.LastSeen).TotalSeconds;
    session.Game.UpgradeRules();
    if (session.Reward is { } ticket && ticket.ExpiresAt <= now) session = session with { Reward = null };
    if (!session.Game.Paused && !session.Game.Completed && !session.Game.OutOfLives)
    {
        if (elapsed > 12) session.Game.Paused = true;
        else if (elapsed >= 1)
        {
            var seconds = (int)elapsed;
            session.Game.Seconds += seconds;
            return session with { LastSeen = session.LastSeen.AddSeconds(seconds) };
        }
        else return session;
    }
    return session with { LastSeen = now };
}
static object View(WebGame session) => new
{
    session.Id, session.Revision, session.Started, difficulty = session.Game.Puzzle.Difficulty,
    givens = session.Game.Puzzle.Givens, session.Game.Values, session.Game.Notes,
    session.Game.Seconds, session.Game.Mistakes, session.Game.Hints, session.Game.Selected,
    session.Game.Paused, session.Game.Completed, session.Game.Lives, session.Game.OutOfLives,
    session.Game.HintCredits, session.Game.CandidateCredits, session.Game.CandidateUses, session.Game.LifeRestores,
    session.Game.CurrentScore, session.Game.NextQuest, session.Game.AdsWatched,
    reward = session.Reward is { } t ? new { t.Kind, remaining = Math.Max(0, (int)Math.Ceiling((t.ReadyAt - DateTimeOffset.UtcNow).TotalSeconds)) } : null,
    canUndo = session.Game.UndoStack.Count > 0,
    canRedo = session.Game.RedoStack.Count > 0
};
app.MapGet("/api/web/session", (HttpContext c, ServerStore s) =>
{
    if (s.WebUser(Token(c)) is not { } name) return Results.Unauthorized();
    lock (s.Gate)
    {
        if (s.Session(name) is not { } session) return Results.Ok(new { empty = true });
        bool paused = session.Game.Paused;
        session = Tick(session);
        if (session.Game.Paused != paused) session = session with { Revision = session.Revision + 1 };
        s.SaveSession(name, session);
        return Results.Ok(View(session));
    }
});
app.MapPost("/api/web/new", async (HttpContext c, ServerStore s, NewGameRequest request) =>
{
    if (s.WebUser(Token(c)) is not { } name) return Results.Unauthorized();
    if (!Enum.IsDefined(request.Difficulty)) return Results.BadRequest();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(25));
    Puzzle puzzle;
    try { puzzle = await Task.Run(() => Sudoku.Generate(request.Difficulty, timeout.Token), timeout.Token); }
    catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
    { return Results.Problem("Генерацію не завершено.", statusCode: 503); }
    lock (s.Gate)
    {
        var previous = s.Session(name);
        if ((previous?.Revision ?? 0) != request.Revision || previous?.Id != request.Id)
            return Results.Conflict();
        GameRecord? abandoned = previous is not null && !previous.Game.Completed
            ? new(previous.Id, previous.Started, DateTimeOffset.UtcNow, "web", "abandoned", Tick(previous).Game) : null;
        var now = DateTimeOffset.UtcNow;
        var next = new WebGame(Guid.NewGuid(), now, Game.Start(puzzle), now, 1);
        s.SaveSession(name, next, abandoned); return Results.Ok(View(next));
    }
});
app.MapPost("/api/web/action", (HttpContext c, ServerStore s, MoveRequest request) =>
{
    if (s.WebUser(Token(c)) is not { } name) return Results.Unauthorized();
    lock (s.Gate)
    {
        if (s.Session(name) is not { } session || session.Id != request.Id ||
            session.Revision != request.Revision) return Results.Conflict();
        bool paused = session.Game.Paused;
        session = Tick(session);
        var game = session.Game;
        bool changed = paused != game.Paused;
        bool wasComplete = game.Completed;
        switch (request.Action)
        {
            case "heartbeat": break;
            case "reward-start":
                if (!game.CanReward(request.Reward ?? "")) return Results.BadRequest(new { error = "Цей ресурс уже заповнено або гра завершена." });
                if (session.Reward is not null && session.Reward.Kind != request.Reward)
                    return Results.BadRequest(new { error = "Спочатку завершіть або скасуйте попередню винагороду." });
                if (session.Reward is null)
                {
                    var now = DateTimeOffset.UtcNow;
                    session = session with { Reward = new RewardTicket(request.Reward!, now.AddSeconds(Game.RewardSeconds), now.AddMinutes(2)) };
                    changed = true;
                }
                break;
            case "reward-claim":
                if (session.Reward is not { } reward || reward.Kind != request.Reward ||
                    reward.ReadyAt > DateTimeOffset.UtcNow || reward.ExpiresAt <= DateTimeOffset.UtcNow)
                    return Results.BadRequest(new { error = "Винагорода ще не готова або термін її дії минув." });
                if (!game.GrantReward(reward.Kind)) return Results.BadRequest(new { error = "Ресурс уже заповнено." });
                session = session with { Reward = null }; changed = true; break;
            case "reward-cancel":
                if (session.Reward is not null) { session = session with { Reward = null }; changed = true; }
                break;
            case "pause": if (!game.Paused && !game.Completed) { game.Paused = true; changed = true; } break;
            case "resume": if (game.Paused && !game.Completed && !game.OutOfLives) { game.Paused = false; changed = true; } break;
            case "enter":
                if (!game.Paused && !game.Completed && game.Editable(request.Cell))
                {
                    game.Selected = request.Cell;
                    changed |= game.Enter(request.Cell, request.Digit, request.Note);
                }
                break;
            case "undo": changed |= game.Undo(); break;
            case "redo": changed |= game.Redo(); break;
            case "candidates": changed |= game.FillNotes(); break;
            case "hint":
                if (game.GetHint() is { } hint)
                { game.Selected = hint.Cell; changed |= game.Enter(hint.Cell, hint.Digit, hint: true); }
                break;
            default: return Results.BadRequest();
        }
        if (changed) session = session with { Revision = session.Revision + 1 };
        GameRecord? completed = !wasComplete && game.Completed
            ? new(session.Id, session.Started, DateTimeOffset.UtcNow, "web", "completed", game) : null;
        if (completed is not null) { completed.Game.UndoStack.Clear(); completed.Game.RedoStack.Clear(); }
        s.SaveSession(name, session, completed);
        return Results.Ok(View(session));
    }
});
app.Run();

public sealed record NewGameRequest(Difficulty Difficulty, Guid? Id, long Revision);
public sealed record MoveRequest(Guid Id, long Revision, string Action, int Cell = 0, int Digit = 0, bool Note = false, string? Reward = null);
