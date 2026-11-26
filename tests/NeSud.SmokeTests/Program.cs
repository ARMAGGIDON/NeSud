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
