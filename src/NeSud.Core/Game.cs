namespace NeSud.Core;

public sealed record Snapshot(int[] Values, int[] Notes);
public sealed class Game
{
    public Puzzle Puzzle { get; set; } = null!;
    public int[] Values { get; set; } = new int[81];
    public int[] Notes { get; set; } = new int[81];
    public List<Snapshot> UndoStack { get; set; } = [];
    public List<Snapshot> RedoStack { get; set; } = [];
    public int Seconds { get; set; }
    public int Mistakes { get; set; }
    public int Hints { get; set; }
    public int Selected { get; set; }
    public bool NotesMode { get; set; }
    public bool Paused { get; set; }
    public bool Awarded { get; set; }
    public bool Completed => Values.SequenceEqual(Puzzle.Solution);

    public static Game Start(Puzzle puzzle) => new()
    {
        Puzzle = puzzle, Values = (int[])puzzle.Givens.Clone(),
        Selected = Array.FindIndex(puzzle.Givens, v => v == 0)
    };

    private Snapshot Capture() => new((int[])Values.Clone(), (int[])Notes.Clone());
    private void Remember()
    {
        UndoStack.Add(Capture());
        if (UndoStack.Count > 150) UndoStack.RemoveAt(0);
        RedoStack.Clear();
    }
    private void Restore(Snapshot snapshot) =>
        (Values, Notes) = ((int[])snapshot.Values.Clone(), (int[])snapshot.Notes.Clone());
    public bool Undo()
    {
        if (Paused || Completed || UndoStack.Count == 0) return false;
        RedoStack.Add(Capture());
        Restore(UndoStack[^1]); UndoStack.RemoveAt(UndoStack.Count - 1);
        return true;
    }
    public bool Redo()
    {
        if (Paused || Completed || RedoStack.Count == 0) return false;
        UndoStack.Add(Capture());
        Restore(RedoStack[^1]); RedoStack.RemoveAt(RedoStack.Count - 1);
        return true;
    }

    public bool Editable(int i) => i is >= 0 and < 81 && Puzzle.Givens[i] == 0 && !Paused && !Completed;
    public bool Enter(int i, int digit, bool note = false, bool autoClean = true, bool hint = false)
    {
        if (!Editable(i) || digit is < 0 or > 9) return false;
        if (note && digit != 0)
        {
            if (Values[i] != 0) return false;
            Remember(); Notes[i] ^= 1 << digit;
            return true;
        }
        if (Values[i] == digit && Notes[i] == 0) return false;
        Remember();
        Values[i] = digit; Notes[i] = 0;
        if (hint) Hints++;
        else if (digit != 0 && digit != Puzzle.Solution[i]) Mistakes++;
        if (autoClean && digit != 0 && digit == Puzzle.Solution[i])
            foreach (int peer in Sudoku.Peers[i]) Notes[peer] &= ~(1 << digit);
        return true;
    }

    public bool FillNotes()
    {
        if (Paused || Completed) return false;
        var next = Enumerable.Range(0, 81).Select(i => Values[i] == 0 ? Sudoku.Candidates(Values, i) : 0).ToArray();
        if (next.SequenceEqual(Notes)) return false;
        Remember(); Notes = next; return true;
    }

    public (int Cell, int Digit, string Reason)? GetHint()
    {
        if (Paused || Completed) return null;
        int wrong = -1;
        for (int i = 0; i < 81; i++)
            if (Values[i] != 0 && Values[i] != Puzzle.Solution[i]) { wrong = i; break; }
        if (wrong >= 0) return (wrong, Puzzle.Solution[wrong], "Виправлення помилки");
        if (Sudoku.LogicalMove(Values) is { } m) return (m.Cell, m.Digit, m.Technique);
        int cell = Values[Selected] == 0 ? Selected : Array.IndexOf(Values, 0);
        return cell < 0 ? null : (cell, Puzzle.Solution[cell], "Відкриття клітинки");
    }

    public void Restart()
    {
        Values = (int[])Puzzle.Givens.Clone(); Notes = new int[81];
        UndoStack.Clear(); RedoStack.Clear(); Seconds = Mistakes = Hints = 0;
        Paused = NotesMode = false; Selected = Array.IndexOf(Values, 0);
    }

    public bool Valid()
    {
        if (Puzzle is null || Puzzle.Givens is null || Puzzle.Solution is null ||
            !Enum.IsDefined(Puzzle.Difficulty) || !Sudoku.IsConsistent(Puzzle.Givens) ||
            !Sudoku.IsConsistent(Puzzle.Solution) || Puzzle.Solution.Contains(0) ||
            Seconds < 0 || Mistakes < 0 || Hints < 0 || Selected is < 0 or >= 81 ||
            UndoStack is null || RedoStack is null || UndoStack.Count > 150 || RedoStack.Count > 150)
            return false;
        if (Enumerable.Range(0, 81).Any(i => Puzzle.Givens[i] != 0 && Puzzle.Givens[i] != Puzzle.Solution[i]))
            return false;
        bool ValidSnapshot(Snapshot? s) => s is not null && s.Values?.Length == 81 && s.Notes?.Length == 81 &&
            Enumerable.Range(0, 81).All(i => s.Values[i] is >= 0 and <= 9 &&
                (Puzzle.Givens[i] == 0 || s.Values[i] == Puzzle.Givens[i]) &&
                (s.Notes[i] & ~Sudoku.All) == 0 && (s.Values[i] == 0 || s.Notes[i] == 0));
        return ValidSnapshot(new Snapshot(Values, Notes)) && UndoStack.All(ValidSnapshot) &&
            RedoStack.All(ValidSnapshot) && Sudoku.CountSolutions(Puzzle.Givens) == 1;
    }
}
