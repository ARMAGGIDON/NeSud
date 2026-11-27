using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestGame(List<Puzzle> puzzles)
    {
    var g = Game.Start(puzzles[0]);
        int cell = g.Selected, correct = g.Puzzle.Solution[cell];
        int given = Array.FindIndex(g.Puzzle.Givens, v => v != 0);
        Check(!g.Enter(given, 0), "Immutable given");
        Check(g.Enter(cell, correct, note: true), "Note entry");
        Check(g.Notes[cell] == 1 << correct, "Note mask");
        Check(g.Enter(cell, correct), "Number entry");
        Check(g.Notes[cell] == 0, "Clear notes after number");
        int mistakesBefore = g.Mistakes, movesBefore = g.UndoStack.Count;
        Check(!g.Editable(cell) && !g.Enter(cell, 0) && !g.Enter(cell, correct % 9 + 1) &&
            !g.Enter(cell, correct, note: true), "Correct answer cannot be overwritten, erased or annotated");
        Check(g.Values[cell] == correct && g.Mistakes == mistakesBefore && g.UndoStack.Count == movesBefore,
            "Blocked edit has no side effects");
        int[] focusBoard = new int[81];
        focusBoard[0] = focusBoard[12] = focusBoard[40] = 5;
        Check(BoardFocus.Nearest(focusBoard, 11, 5) == 12 &&
            BoardFocus.Nearest(focusBoard, 12, 5) == 0 &&
            BoardFocus.Nearest(focusBoard, 12, 5, false) == 12 &&
            BoardFocus.Nearest(focusBoard, 12, 7) == -1, "Digit navigation and missing digit");
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
            var state = new AppState { Game = g, Dark = false, ShowMistakes = false, PaintDigit = true };
            store.Save(state);
            var loaded = store.Load();
            Check(loaded.Game is { } restored && restored.Valid() && restored.Seconds == 123 &&
                restored.NotesMode && restored.Values.SequenceEqual(g.Values) &&
                restored.Notes.SequenceEqual(g.Notes) && restored.UndoStack.Count == g.UndoStack.Count &&
                !loaded.Dark && !loaded.ShowMistakes && loaded.PaintDigit, "Persistence round trip");
            g.Seconds = 124;
            store.Save(state);
            File.WriteAllText(path, "broken");
            Check(store.Load().Game?.Seconds == 123, "Backup recovery");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        var paint = Game.Start(puzzles[0]);
        var emptyCells = Enumerable.Range(0, 81).Where(i => paint.Values[i] == 0).Take(2).ToArray();
        Check(paint.Enter(emptyCells[0], 5, note: true) && paint.Enter(emptyCells[1], 5, note: true) &&
            paint.Notes[emptyCells[0]] == 1 << 5 && paint.Notes[emptyCells[1]] == 1 << 5 &&
            paint.Enter(emptyCells[0], 5, note: true) && paint.Notes[emptyCells[0]] == 0 &&
            paint.Notes[emptyCells[1]] == 1 << 5 && paint.Mistakes == 0, "Repeated digit note painting toggles independently");
        g.Restart();
        Check(g.Values.SequenceEqual(g.Puzzle.Givens) && g.Seconds == 0 && g.UndoStack.Count == 0, "Restart");
        for (int i = 0; i < 81; i++) if (g.Puzzle.Givens[i] == 0) g.Enter(i, g.Puzzle.Solution[i]);
        Check(g.Completed && !g.Enter(cell, 0), "Completion");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            Sudoku.Generate(Difficulty.Easy, cancelled.Token);
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS game, history, persistence, recovery, completion, cancellation");
    }
}
