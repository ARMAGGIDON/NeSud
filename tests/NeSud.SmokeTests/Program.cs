using NeSud.Core;
using NeSud.Storage;

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
Check(lifeGame.GrantReward("life") && lifeGame.Lives == 1 && !lifeGame.OutOfLives &&
    lifeGame.CurrentScore == initialScore - 325, "Life restoration and score penalty");
Check(lifeGame.Undo() && lifeGame.Lives == 1 && lifeGame.Mistakes == 3, "Undo never refunds lives");
var aid = Game.Start(puzzles[0]);
Check(aid.FillNotes() && aid.CandidateCredits == 0 && aid.CandidateUses == 1 && aid.CurrentScore == initialScore - 100,
    "Candidate credits and penalty");
Check(aid.Undo() && aid.CandidateCredits == 0 && !aid.FillNotes(), "Undo never refunds aid");
var hm = aid.GetHint()!.Value;
Check(aid.Enter(hm.Cell, hm.Digit, hint: true) && aid.HintCredits == 0 && aid.GetHint() is null, "Hint exhaustion");
foreach (int i in blanks.Where(i => i != hm.Cell).Take(15)) Check(aid.Enter(i, aid.Puzzle.Solution[i]), "Quest progress");
Check(aid.HintCredits == 1 && aid.CandidateCredits == 1 && aid.ManualCorrect.Count == 15, "Milestone rewards");
Check(aid.Undo(), "Quest undo");
int last = blanks.Where(i => i != hm.Cell).Take(15).Last();
Check(aid.Enter(last, aid.Puzzle.Solution[last]) && aid.HintCredits == 1 && aid.CandidateCredits == 1, "No quest replay");
var copied = Database.Decode<Game>(Database.Encode(aid));
Check(copied.Valid() && copied.CandidateUses == 1 && copied.ManualCorrect.Count == 15 &&
    copied.CurrentScore == aid.CurrentScore, "Resource persistence");
Console.WriteLine("PASS lives, reward limits, aid, score, quest anti-replay, resource persistence");
