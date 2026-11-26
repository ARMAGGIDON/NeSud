using NeSud.Core;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        app.MapGet("/api/web/session", (HttpContext c, ServerStore s, bool? pause, string? challengeDay) =>
        {
            if (s.WebUser(Token(c)) is not { } name) return Results.Unauthorized();
            if (challengeDay is not null && (ServerStore.IsGuest(name) || !s.ChallengeAllowed(challengeDay))) return Results.BadRequest(new { error = "Дата щоденної гри недоступна." });
            lock (s.Gate)
            {
                if (s.Session(name, challengeDay) is not { } session) return Results.Ok(new { empty = true });
                bool paused = session.Game.Paused;
                session = Tick(session);
                if (pause == true && !session.Game.Completed && !session.Game.OutOfLives) session.Game.Paused = true;
                if (session.Game.Paused != paused) session = session with { Revision = session.Revision + 1 };
                s.SaveSession(name, session, session.Game.FinalDefeat(DateTimeOffset.UtcNow) ? new(session.Id, session.Started, DateTimeOffset.UtcNow, "web", "defeated", session.Game) : null);
                return Results.Ok(View(session));
            }
        });
        app.MapPost("/api/web/new", async (HttpContext c, ServerStore s, NewGameRequest request) =>
        {
            if (s.WebUser(Token(c)) is not { } name) return Results.Unauthorized();
            if (!Enum.IsDefined(request.Difficulty)) return Results.BadRequest();
            if (request.ChallengeDay is { } day && (ServerStore.IsGuest(name) || !s.ChallengeAllowed(day))) return Results.BadRequest(new { error = "Дата щоденної гри недоступна." });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            Puzzle puzzle;
            try
            {
                puzzle = await Task.Run(() => request.ChallengeDay is { } date ? s.ChallengePuzzle(date, timeout.Token) : Sudoku.Generate(request.Difficulty, timeout.Token), timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
            { return Results.Problem("Генерацію не завершено.", statusCode: 503); }
            lock (s.Gate)
            {
                if (request.ChallengeDay is { } date && (!s.ChallengeAllowed(date) || s.ChallengeWon(name, date))) return Results.Conflict(new { error = "Щоденне завдання вже завершене або недоступне." });
                var previous = s.Session(name, request.ChallengeDay);
                if ((previous?.Revision ?? 0) != request.Revision || previous?.Id != request.Id)
                    return Results.Conflict();
                GameRecord? abandoned = previous is not null && !previous.Game.Completed
                    ? new(previous.Id, previous.Started, DateTimeOffset.UtcNow, "web", previous.Game.OutOfLives ? "defeated" : "abandoned", Tick(previous).Game) : null;
                var now = DateTimeOffset.UtcNow;
                var next = new WebGame(Guid.NewGuid(), now, Game.Start(puzzle), now, 1, ChallengeDay: request.ChallengeDay);
                next.Game.AutoFinish = request.AutoFinish;
                s.SaveSession(name, next, abandoned);
                return Results.Ok(View(next));
            }
        });
        app.MapPost("/api/web/action", (HttpContext c, ServerStore s, MoveRequest request) =>
        {
            if (s.WebUser(Token(c)) is not { } name) return Results.Unauthorized();
            if (request.ChallengeDay is { } day && (ServerStore.IsGuest(name) || !s.ChallengeAllowed(day))) return Results.BadRequest(new { error = "Дата щоденної гри недоступна." });
            lock (s.Gate)
            {
                if (s.Session(name, request.ChallengeDay) is not { } session || session.Id != request.Id ||
                    session.Revision != request.Revision) return Results.Conflict();
                bool paused = session.Game.Paused;
                session = Tick(session);
                var game = session.Game;
                bool changed = paused != game.Paused;
                bool wasComplete = game.Completed;
                string? walletKind = null;
                if (session.Reward is not null && request.Action is not ("heartbeat" or "pause" or "reward-start" or "reward-claim" or "reward-cancel"))
                    return Results.BadRequest(new { error = "Спочатку завершіть або скасуйте винагороду." });
                switch (request.Action)
                {
                    case "heartbeat": break;
                    case "wallet":
                        if (request.Reward == "life" || ServerStore.IsGuest(name) || !game.CanReward(request.Reward ?? "") || !s.HasWallet(name, request.Reward!))
                            return Results.Conflict(new { error = "Ресурс недоступний." });
                        if (game.GrantReward(request.Reward!, advertisement: false))
                        {
                            walletKind = request.Reward;
                            changed = true;
                        }
                        break;
                    case "reward-start":
                        if (request.Reward == "life" ? !game.CanStartRevive(DateTimeOffset.UtcNow) : game.OutOfLives || !game.CanAdReward(request.Reward ?? "") || !ServerStore.IsGuest(name) && s.HasWallet(name, request.Reward!)) return Results.BadRequest(new { error = "Цей ресурс уже заповнено або гра завершена." });
                        if (session.Reward is not null && session.Reward.Kind != request.Reward)
                            return Results.BadRequest(new { error = "Спочатку завершіть або скасуйте попередню винагороду." });
                        if (session.Reward is null)
                        {
                            var now = DateTimeOffset.UtcNow;
                            if (request.Reward == "life" && !game.BeginRevive(now)) return Results.BadRequest();
                            session = session with { Reward = new RewardTicket(request.Reward!, now.AddSeconds(Game.RewardSeconds), now.AddMinutes(2)) };
                            changed = true;
                        }
                        break;
                    case "reward-claim":
                        if (session.Reward is not { } reward || reward.Kind != request.Reward ||
                            reward.ReadyAt > DateTimeOffset.UtcNow || reward.ExpiresAt <= DateTimeOffset.UtcNow)
                            return Results.BadRequest(new { error = "Винагорода ще не готова або термін її дії минув." });
                        if (!(reward.Kind == "life" ? game.FinishRevive(DateTimeOffset.UtcNow) : (ServerStore.IsGuest(name) || !s.HasWallet(name, reward.Kind)) && game.GrantReward(reward.Kind))) return Results.BadRequest(new { error = "Ресурс уже заповнено." });
                        session = session with { Reward = null, LastSeen = DateTimeOffset.UtcNow };
                        changed = true;
                        break;
                    case "reward-cancel":
                        if (session.Reward is not null)
                        {
                            if (session.Reward.Kind == "life") game.CancelRevive();
                            session = session with { Reward = null, LastSeen = DateTimeOffset.UtcNow };
                            changed = true;
                        }
                        break;
                    case "pause": if (!game.Paused && !game.Completed)
                    {
                        game.Paused = true;
                        changed = true;
                    }
                    break;
                    case "resume": if (game.Paused && !game.Completed && !game.OutOfLives)
                    {
                        game.Paused = false;
                        game.TryAutoFinish();
                        changed = true;
                    }
                    break;
                    case "auto-finish":
                        if (!game.Completed && !game.OutOfLives)
                        {
                            game.AutoFinish = request.Note;
                            game.TryAutoFinish();
                            changed = true;
                        }
                        break;
                    case "enter":
                        if (!game.Paused && !game.Completed && game.Editable(request.Cell))
                        {
                            game.Selected = request.Cell;
                            bool entered = game.Enter(request.Cell, request.Digit, request.Note);
                            if (entered && request.Digit > 0 && !request.Note) session = session with { Audit = (session.Audit ?? new MoveAudit()).Record(DateTimeOffset.UtcNow) };
                            changed |= entered;
                        }
                        break;
                    case "undo": changed |= game.Undo();
                    break;
                    case "redo": changed |= game.Redo();
                    break;
                    case "candidates": changed |= game.FillNotes();
                    break;
                    case "hint":
                        if (game.GetHint() is { } hint)
                        { game.Selected = hint.Cell;
                        changed |= game.Enter(hint.Cell, hint.Digit, hint: true); }
                        break;
                    default: return Results.BadRequest();
                }
                if (changed) session = session with { Revision = session.Revision + 1 };
                GameRecord? completed = !wasComplete && game.Completed
                    ? new(session.Id, session.Started, DateTimeOffset.UtcNow, "web", "completed", game) : null;
                if (completed is not null)
                {
                    completed.Game.UndoStack.Clear();
                    completed.Game.RedoStack.Clear();
                }
                var terminal = completed ?? (game.FinalDefeat(DateTimeOffset.UtcNow) ? new GameRecord(session.Id, session.Started, DateTimeOffset.UtcNow, "web", "defeated", game) : null);
                s.SaveSession(name, session, terminal, walletKind);
                if (request.Action == "heartbeat" && !changed && terminal is null && !game.OutOfLives && session.Reward is null)
                    return Results.Ok(new { pulseOnly = true, session.Id, session.Revision, game.Seconds, clockFraction = session.Fraction, game.CurrentScore });
                return Results.Ok(View(session));
            }
        });
    }

    private static WebGame Tick(WebGame session)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - session.LastSeen).TotalSeconds;
        session.Game.UpgradeRules();
        if (session.Reward is { } ticket && ticket.ExpiresAt <= now)
        {
            if (ticket.Kind == "life") session.Game.CancelRevive();
            session = session with { Reward = null, LastSeen = now };
            elapsed = 0;
        }
        if (!session.Game.Paused && !session.Game.Completed && !session.Game.OutOfLives)
        {
            if (elapsed > 12) session.Game.Paused = true;
            else if (session.Reward is null)
            {
                double total = Math.Max(0, elapsed) + session.Fraction;
                int seconds = (int) total;
                session.Game.Seconds += seconds;
                return session with { LastSeen = now, Fraction = total - seconds };
            }
        }
        return session with { LastSeen = now };
    }
    private static object View(WebGame session) => new
    {
        session.Id, session.Revision, session.Started, session.ChallengeDay, difficulty = session.Game.Puzzle.Difficulty,
        givens = session.Game.Puzzle.Givens, session.Game.Values, session.Game.Notes,
        session.Game.Seconds, clockFraction = session.Fraction, session.Game.Mistakes, session.Game.Hints, session.Game.Selected,
        session.Game.Paused, session.Game.Completed, session.Game.Lives, session.Game.OutOfLives,
        session.Game.HintCredits, session.Game.CandidateCredits, session.Game.CandidateUses, session.Game.LifeRestores,
        session.Game.CurrentScore, session.Game.RulesVersion, session.Game.BaseScore, session.Game.SpeedBonus, session.Game.AutoFinish, session.Game.AutoFilled, session.Game.AutoFillPenalty,
        reviveSeconds = session.Game.ReviveSeconds(DateTimeOffset.UtcNow),
        reviveRemainingMs = session.Game.CanStartRevive(DateTimeOffset.UtcNow) ? Math.Max(0, (session.Game.ReviveDeadline!.Value - DateTimeOffset.UtcNow).TotalMilliseconds) : 0,
        finalDefeat = session.Game.FinalDefeat(DateTimeOffset.UtcNow),
        wrong = Enumerable.Range(0, 81).Select(i => session.Game.Values[i] != 0 && session.Game.Values[i] != session.Game.Puzzle.Solution[i]).ToArray(),
        remaining = Enumerable.Range(1, 9).Select(session.Game.Remaining).ToArray(),
        session.Game.CandidateChanges, session.Game.EffectiveCandidatePenalty,
        candidateDelta = session.Game.CandidateDelta(), session.Game.AdsWatched,
        reward = session.Reward is { } t ? new { t.Kind, remaining = Math.Max(0, (int) Math.Ceiling((t.ReadyAt - DateTimeOffset.UtcNow).TotalSeconds)) } : null,
        canUndo = session.Game.UndoStack.Count > 0,
        canRedo = session.Game.RedoStack.Count > 0
    };
}
