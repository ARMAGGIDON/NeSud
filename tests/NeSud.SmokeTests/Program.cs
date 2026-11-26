using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

static void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
}

int repetitions = args.Length > 0 ? int.Parse(args[0]) : 3;
var random = new Random(2036);
var puzzles = new List<Puzzle>();
foreach (var level in Enum.GetValues<Difficulty>())
{
    for (int i = 0; i < repetitions; i++)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var puzzle = Sudoku.Generate(level, timeout.Token, random);
        var (min, max) = Sudoku.ClueBand(level);
        int clues = puzzle.Givens.Count(v => v != 0);
        Check(clues >= min && clues <= max, "Clue band");
        Check(Sudoku.IsConsistent(puzzle.Solution) && !puzzle.Solution.Contains(0), "Complete valid solution");
        Check(Sudoku.CountSolutions(puzzle.Givens, token: timeout.Token) == 1, "Unique puzzle");
        Check(Game.Start(puzzle).Valid(), "Initial game validation");
        if (level is Difficulty.Easy or Difficulty.Medium)
            Check(Sudoku.SolvableBySingles(puzzle.Givens), "Solvable by singles");
        if (level == Difficulty.Expert)
            Check(!Sudoku.SolvableBySingles(puzzle.Givens), "Expert exceeds singles");
        puzzles.Add(puzzle);
        Console.WriteLine($"PASS {level}: {clues} clues");
    }
}
Check(puzzles.Select(p => string.Join("", p.Givens)).Distinct().Count() == puzzles.Count, "Variety");
var g = Game.Start(puzzles[0]);
int cell = g.Selected, correct = g.Puzzle.Solution[cell];
int given = Array.FindIndex(g.Puzzle.Givens, v => v != 0);
Check(!g.Enter(given, 0), "Immutable given");
Check(g.Enter(cell, correct, note: true), "Note entry");
Check(g.Notes[cell] == 1 << correct, "Note mask");
Check(g.Enter(cell, correct), "Number entry");
Check(g.Notes[cell] == 0, "Clear notes after number");
Check(g.Undo() && g.Values[cell] == 0 && g.Notes[cell] != 0, "Undo notes");
Check(g.Redo() && g.Values[cell] == correct, "Redo number");
Check(g.Undo() && g.Enter(cell, correct % 9 + 1), "Branch history");
Check(g.RedoStack.Count == 0 && g.Mistakes == 1, "Clear redo and count mistake");
Check(g.GetHint() is { } h && h.Cell == cell && h.Digit == correct, "Corrective hint");
Check(g.Enter(cell, correct, hint: true) && g.Hints == 1, "Apply hint");
g.Paused = true;
Check(!g.Enter(cell, 0) && !g.Undo() && !g.FillNotes(), "Pause guard");
g.Paused = false;
Check(g.FillNotes(), "Fill candidates");
g.Seconds = 123;
g.NotesMode = true;
var folder = Path.Combine(Path.GetTempPath(), "NeSud-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var path = Path.Combine(folder, "state.json");
    var store = new StateStore(path);
    var state = new AppState { Game = g, Dark = false, ShowMistakes = false };
    store.Save(state);
    var loaded = store.Load();
    Check(loaded.Game is { } restored && restored.Valid() && restored.Seconds == 123 &&
        restored.NotesMode && restored.Values.SequenceEqual(g.Values) &&
        restored.Notes.SequenceEqual(g.Notes) && restored.UndoStack.Count == g.UndoStack.Count &&
        !loaded.Dark && !loaded.ShowMistakes, "Persistence round trip");
    g.Seconds = 124;
    store.Save(state);
    File.WriteAllText(path, "broken");
    Check(store.Load().Game?.Seconds == 123, "Backup recovery");
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
g.Restart();
Check(g.Values.SequenceEqual(g.Puzzle.Givens) && g.Seconds == 0 && g.UndoStack.Count == 0, "Restart");
for (int i = 0; i < 81; i++) if (g.Puzzle.Givens[i] == 0) g.Enter(i, g.Puzzle.Solution[i]);
Check(g.Completed && !g.Enter(cell, 0), "Completion");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { Sudoku.Generate(Difficulty.Easy, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { }
Console.WriteLine("PASS game, history, persistence, recovery, completion, cancellation");

var dbDir = Path.Combine(Path.GetTempPath(), "NeSud-db-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var local = new LocalStore(Path.Combine(dbDir, "client.db"), Path.Combine(dbDir, "legacy.json"));
    using (var connection = new Database(Path.Combine(dbDir, "client.db")).Open())
    using (var versionQuery = Database.Command(connection, "SELECT sqlite_version()"))
    {
        var version = Version.Parse((string)versionQuery.ExecuteScalar()!);
        Check(version >= new Version(3, 50, 2), "Native SQLite must include the CVE-2025-6965 fix");
        Console.WriteLine($"PASS native SQLite {version}");
    }
    var attempt = Game.Start(puzzles[0]);
    var appState = new AppState { Game = attempt, GameId = Guid.NewGuid(), Started = DateTimeOffset.UtcNow };
    var record = new GameRecord(appState.GameId, appState.Started, DateTimeOffset.UtcNow, "desktop", "abandoned", attempt);
    local.Save(appState, record, "account-a");
    local.Save(appState, record, "account-a");
    Check(local.History("account-a", true).Count == 1, "Idempotent local history");
    Check(local.History("account-b").Count == 0, "Account isolation");
    local.Merge("account-a", [record]);
    Check(local.History("account-a", true).Count == 0 && local.History("account-a").Count == 1, "Sync acknowledgment");
    Check(local.Load().GameId == appState.GameId, "SQLite current game");
    Console.WriteLine("PASS local SQLite, account isolation, history and synchronization");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(dbDir)) Directory.Delete(dbDir, true); }

var lifeGame = Game.Start(puzzles[0]);
var blanks = Enumerable.Range(0, 81).Where(i => lifeGame.Puzzle.Givens[i] == 0).ToArray();
int initialScore = lifeGame.CurrentScore;
for (int i = 0; i < 3; i++) Check(lifeGame.Enter(blanks[i], lifeGame.Puzzle.Solution[blanks[i]] % 9 + 1), "Wrong move");
Check(lifeGame.Lives == 0 && lifeGame.OutOfLives && lifeGame.Mistakes == 3, "Life exhaustion");
Check(!lifeGame.Undo() && !lifeGame.Redo() && !lifeGame.Enter(blanks[3], lifeGame.Puzzle.Solution[blanks[3]])
    && !lifeGame.FillNotes() && lifeGame.GetHint() is null, "Exhaustion blocks actions");
Check(lifeGame.CurrentScore == initialScore - 225, "Mistake score penalty");
var lossTime = lifeGame.ReviveDeadline!.Value.AddSeconds(-10);
Check(!lifeGame.GrantReward("life") && lifeGame.CanStartRevive(lossTime) && !lifeGame.CanStartRevive(lossTime.AddSeconds(10)), "Revival is limited to defeat and ten seconds");
Check(lifeGame.BeginRevive(lossTime) && !lifeGame.BeginRevive(lossTime) && !lifeGame.FinishRevive(lossTime.AddSeconds(7)), "Single ad attempt and early claim denied");
Check(lifeGame.FinishRevive(lossTime.AddSeconds(8)) && lifeGame.Lives == 1 && !lifeGame.OutOfLives &&
    lifeGame.CurrentScore == initialScore - 325, "Life restoration and score penalty");
Check(lifeGame.Undo() && lifeGame.Lives == 1 && lifeGame.Mistakes == 3, "Undo never refunds lives");
var aid = Game.Start(puzzles[0]);
int blank = blanks[0], correctMask = Sudoku.Candidates(aid.Values, blank);
int validDigit = BitOperations.TrailingZeroCount((uint)correctMask);
int invalidDigit = BitOperations.TrailingZeroCount((uint)(Sudoku.All & ~correctMask));
Check(aid.Enter(blank, validDigit, note: true) && aid.Enter(blank, invalidDigit, note: true), "Manual notes setup");
int expected = Enumerable.Range(0, 81).Sum(j => aid.Values[j] == 0
    ? BitOperations.PopCount((uint)(aid.Notes[j] ^ Sudoku.Candidates(aid.Values, j))) : 0);
Check(aid.CandidateDelta() == expected && aid.FillNotes() && aid.CandidateCredits == 0 &&
    aid.CandidateUses == 1 && aid.CandidateChanges == expected &&
    aid.CurrentScore == Math.Max(0, initialScore - 2 * expected), "Candidate additions and corrections");
Check(aid.CandidateDelta() == 0 && aid.GrantReward("candidates", advertisement: false), "Wallet credit");
Check(!aid.FillNotes() && aid.CandidateCredits == 1, "No-op costs nothing");
Check(aid.Undo() && aid.CandidateCredits == 1 && aid.CandidatePenalty == 2 * expected, "Undo never refunds aid");
var hm = aid.GetHint()!.Value;
Check(aid.Enter(hm.Cell, hm.Digit, hint: true) && aid.HintCredits == 0 && aid.GetHint() is null, "Hint exhaustion");
foreach (int j in blanks.Where(j => j != hm.Cell).Take(15)) Check(aid.Enter(j, aid.Puzzle.Solution[j]), "Manual progress");
Check(aid.HintCredits == 0 && aid.CandidateCredits == 1, "Removed old milestone rewards");
var copied = Database.Decode<Game>(Database.Encode(aid));
Check(copied.Valid() && copied.CandidateUses == 1 && copied.CandidateChanges == expected &&
    copied.CurrentScore == aid.CurrentScore, "Resource persistence");
var legacy = Game.Start(puzzles[0]); legacy.RulesVersion = 1; legacy.CandidateUses = 2;
int legacyScore = legacy.CurrentScore; legacy.UpgradeRules();
Check(legacy.RulesVersion == 3 && legacy.CurrentScore == legacyScore, "Legacy migration");
Console.WriteLine("PASS lives, aid, variable penalties, legacy migration, persistence");

var cancelledRevive = Game.Start(puzzles[0]);
Check(!cancelledRevive.CanStartRevive(DateTimeOffset.UtcNow) && !cancelledRevive.GrantReward("life"), "No proactive lives");
for (int j = 0; j < 3; j++) cancelledRevive.Enter(blanks[j], cancelledRevive.Puzzle.Solution[blanks[j]] % 9 + 1);
var startedRevive = cancelledRevive.ReviveDeadline!.Value.AddSeconds(-10);
Check(cancelledRevive.BeginRevive(startedRevive), "Start revival");
cancelledRevive.CancelRevive();
Check(cancelledRevive.FinalDefeat(startedRevive.AddSeconds(1)) &&
    !cancelledRevive.BeginRevive(startedRevive.AddSeconds(1)) && !cancelledRevive.FinishRevive(startedRevive.AddSeconds(8)), "Cancel consumes chance");
var auto = Game.Start(puzzles[0]);
foreach (int j in blanks.Take(blanks.Length-10)) auto.Enter(j, auto.Puzzle.Solution[j]);
auto.AutoFinish = true;
Check(!auto.TryAutoFinish(), "Auto finish not at ten blanks");
int tenth = blanks[blanks.Length-10];
Check(auto.Enter(tenth, auto.Puzzle.Solution[tenth]) && auto.Completed && auto.AutoFilled == 9 &&
    auto.AutoFillPenalty == 180 && auto.Seconds == 0, "Auto finish applies points not time");
Check(auto.Remaining(1) == 0 && auto.Remaining(9) == 0, "Digit counts on completion");
var manual = Game.Start(puzzles[0]);
foreach (int j in blanks) manual.Enter(j, manual.Puzzle.Solution[j]);
manual.Seconds = manual.TargetSeconds/2;
Check(manual.SpeedBonus == (int)Math.Round(manual.BaseScore*0.6*0.25), "Quadratic midpoint");
manual.Seconds = manual.TargetSeconds;
Check(manual.SpeedBonus == 0, "Target speed bonus zero");
var rapid = new MoveAudit();
var nowAudit = DateTimeOffset.UtcNow;
for (int j = 0; j < 20; j++) rapid = rapid.Record(nowAudit.AddMilliseconds(j*30));
Check(rapid.Flag(manual), "Private rapid move audit");
Console.WriteLine("PASS timed single revival, cancellation, auto finish, counts, quadratic bonus and audit");

var dailyDir = Path.Combine(Path.GetTempPath(), "NeSud-daily-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var server = new ServerStore(Path.Combine(dailyDir, "server.db"));
    var user = server.Login(new Credentials("daily-user", "test-password-123", "test-password-123"), true)!.User;
    var other = server.Login(new Credentials("daily-other", "test-password-123", "test-password-123"), true)!.User;
    var first = server.Daily(user);
    Check(first.Packs.Count == 1 && first.Packs[0].Milestone == "login", "Daily login pack");
    Check(server.Daily(user).Packs.Count == 1, "Login idempotency");
    var pack = first.Packs[0]; var choice = new DailyChoice(pack.Day, pack.Milestone, pack.Slot, 1);
    var result = server.Choose(user, choice)!; var again = server.Choose(user, choice)!;
    Check(result.Item == again.Item && result.Status.Packs.Count == 0 && again.Status.Packs.Count == 0 &&
        result.Status.Lives + result.Status.Hints + result.Status.Candidates == result.Item.Amount &&
        again.Status.Lives + again.Status.Hints + again.Status.Candidates == result.Item.Amount, "Idempotent card claim");
    Check(server.Choose(user, choice with { Card = 0 }) is null, "Cannot open another card");
    Check(server.Daily(other).Packs.Count == 1 && server.Daily(other).Lives + server.Daily(other).Hints + server.Daily(other).Candidates == 0, "Wallet isolation");
    var spend = new WalletRequest(Guid.NewGuid(), Guid.NewGuid(), result.Item.Kind);
    Check(server.SpendDesktop(user, spend) is not null && server.SpendDesktop(user, spend) is not null, "Idempotent spend");
    var balance = server.Daily(user);
    Check(balance.Lives + balance.Hints + balance.Candidates == result.Item.Amount - 1, "Debit once");
    var distinct = puzzles.DistinctBy(p => string.Concat(p.Givens)).ToList();
    while (distinct.Count < 7) distinct.Add(Sudoku.Generate(Difficulty.Easy, random: random));
    for (int n = 1; n <= 7; n++)
    {
        var solved = Game.Start(distinct[n - 1]);
        for (int j = 0; j < 81; j++) if (solved.Puzzle.Givens[j] == 0) solved.Enter(j, solved.Puzzle.Solution[j]);
        var now = DateTimeOffset.UtcNow;
        var entry = new GameRecord(Guid.NewGuid(), now, now, "desktop", "completed", solved);
        server.Upload(user, [entry]); server.Upload(user, [entry]);
        var daily = server.Daily(user);
        int count = n >= 7 ? 5 : n >= 3 ? 2 : 1;
        Check(daily.Wins == n && daily.Packs.Count == count, "Daily win milestones " + n);
    }
    var previousDay = DateTimeOffset.UtcNow.AddDays(-1);
    var oldGame = Game.Start(distinct[0]);
    for (int j = 0; j < 81; j++) if (oldGame.Puzzle.Givens[j] == 0) oldGame.Enter(j, oldGame.Puzzle.Solution[j]);
    var late = new GameRecord(Guid.NewGuid(), previousDay, previousDay, "desktop", "completed", oldGame);
    server.Upload(other, [late]); server.Upload(other, [late]);
    var lateStatus = server.Daily(other);
    Check(lateStatus.Wins == 0 && lateStatus.Packs.Count == 2 &&
        lateStatus.Packs.Any(p => p.Milestone == "win1" && p.Day != lateStatus.Day), "Late offline completion credits original day");
    Console.WriteLine("PASS daily login, card choice, wallet isolation, idempotent spend, 1/3/7 milestones, late sync");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(dailyDir)) Directory.Delete(dailyDir, true); }
