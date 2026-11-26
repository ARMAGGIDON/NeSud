using System.Numerics;

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
    public const int MaxLives = 3, MaxAid = 3, RewardSeconds = 8;
    public int RulesVersion { get; set; }
    public int Lives { get; set; } = MaxLives;
    public int HintCredits { get; set; } = 1;
    public int CandidateCredits { get; set; } = 1;
    public int CandidateUses { get; set; }
    public long CandidatePenalty { get; set; }
    public long CandidateChanges { get; set; }
    public const int CandidatePointCost = 2;
    public long EffectiveCandidatePenalty => RulesVersion >= 2 ? CandidatePenalty : RulesVersion == 1 ? 100L * CandidateUses : 0;
    public int LifeRestores { get; set; }
    public bool AutoFinish { get; set; }
    public int AutoFilled { get; set; }
    public const int AutoFillCost = 20;
    public DateTimeOffset? ReviveDeadline { get; set; }
    public bool ReviveAttempted { get; set; }
    public DateTimeOffset? ReviveReadyAt { get; set; }
    public DateTimeOffset? ReviveExpiresAt { get; set; }
    public int BaseScore => 1000 * ((int)Puzzle.Difficulty + 1);
    public int TargetSeconds => Puzzle.Difficulty switch
    { Difficulty.Easy => 480, Difficulty.Medium => 720, Difficulty.Hard => 1200, _ => 1800 };
    public int SpeedBonus => RulesVersion < 3 || !Completed ? 0 :
        (int)Math.Round(BaseScore * 0.6 * Math.Pow(Math.Clamp(1.0 - (double)Seconds / TargetSeconds, 0, 1), 2));
    public long AutoFillPenalty => RulesVersion >= 3 ? (long)AutoFilled * AutoFillCost : 0;
    public bool CanStartRevive(DateTimeOffset now) => RulesVersion >= 3 && OutOfLives && !ReviveAttempted &&
        LifeRestores == 0 && ReviveDeadline is { } deadline && now < deadline;
    public int ReviveSeconds(DateTimeOffset now) => CanStartRevive(now)
        ? Math.Max(0, (int)Math.Ceiling((ReviveDeadline!.Value - now).TotalSeconds)) : 0;
    public bool FinalDefeat(DateTimeOffset now) => OutOfLives && !CanStartRevive(now) &&
        (ReviveExpiresAt is null || ReviveExpiresAt <= now);
    public bool BeginRevive(DateTimeOffset now)
    {
        if (!CanStartRevive(now)) return false;
        ReviveAttempted = true; ReviveReadyAt = now.AddSeconds(RewardSeconds); ReviveExpiresAt = now.AddMinutes(2);
        return true;
    }
    public bool FinishRevive(DateTimeOffset now)
    {
        if (!OutOfLives || !ReviveAttempted || ReviveReadyAt is not { } ready || ReviveExpiresAt is not { } expiry ||
            now < ready || now >= expiry) return false;
        Lives = 1; LifeRestores++; AdsWatched++; CancelRevive(); return true;
    }
    public void CancelRevive() { ReviveReadyAt = ReviveExpiresAt = null; }
    public int Remaining(int digit) => Enumerable.Range(0, 81).Count(i => Puzzle.Solution[i] == digit && Values[i] != digit);
    public bool TryAutoFinish()
    {
        if (RulesVersion < 3 || !AutoFinish || Paused || Completed || OutOfLives) return false;
        int blanks = Values.Count(v => v == 0);
        if (blanks is < 1 or > 9 || Enumerable.Range(0, 81).Any(i => Values[i] != 0 && Values[i] != Puzzle.Solution[i])) return false;
        for (int i = 0; i < 81; i++) if (Values[i] == 0) { Values[i] = Puzzle.Solution[i]; Notes[i] = 0; }
        AutoFilled += blanks; return true;
    }
    public int AdsWatched { get; set; }
    public HashSet<int> ManualCorrect { get; set; } = [];
    public bool OutOfLives => RulesVersion > 0 && Lives == 0 && !Completed;
    public int CurrentScore => (int)Math.Max(0L, (long)BaseScore + SpeedBonus -
        Seconds / 3L - 75L * Mistakes - 150L * Hints -
        EffectiveCandidatePenalty - AutoFillPenalty - (RulesVersion > 0 ? 100L * LifeRestores : 0L));
    // Legacy history retains its original scoring rules; only active games are migrated.
    public void UpgradeRules()
    {
        if (RulesVersion >= 3 || Completed) return;
        if (RulesVersion == 0) { Lives = Math.Max(0, MaxLives - Mistakes); HintCredits = CandidateCredits = 1; }
        CandidatePenalty = EffectiveCandidatePenalty;
        RulesVersion = 3; ReviveAttempted = LifeRestores > 0;
        if (OutOfLives && !ReviveAttempted) ReviveDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
    }

    public bool CanReward(string kind) => !Completed && (RulesVersion < 3 || !OutOfLives) && RulesVersion > 0 && (kind switch
    {
        "life" => RulesVersion < 3 && Lives < MaxLives,
        "hint" => HintCredits < MaxAid,
        "candidates" => CandidateCredits < MaxAid,
        _ => false
    });
    public bool GrantReward(string kind, bool advertisement = true)
    {
        if (!CanReward(kind)) return false;
        switch (kind)
        {
            case "life": Lives++; LifeRestores++; break;
            case "hint": HintCredits++; break;
            case "candidates": CandidateCredits++; break;
        }
        if (advertisement) AdsWatched++; return true;
    }

    public static Game Start(Puzzle puzzle) => new()
    {
        Puzzle = puzzle, Values = (int[])puzzle.Givens.Clone(), RulesVersion = 3,
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
        if (Paused || Completed || OutOfLives || UndoStack.Count == 0) return false;
        RedoStack.Add(Capture());
        Restore(UndoStack[^1]); UndoStack.RemoveAt(UndoStack.Count - 1); TryAutoFinish();
        return true;
    }
    public bool Redo()
    {
        if (Paused || Completed || OutOfLives || RedoStack.Count == 0) return false;
        UndoStack.Add(Capture());
        Restore(RedoStack[^1]); RedoStack.RemoveAt(RedoStack.Count - 1); TryAutoFinish();
        return true;
    }

    public bool Editable(int i) => i is >= 0 and < 81 && Puzzle.Givens[i] == 0 && !Paused && !Completed && !OutOfLives;
    public bool Enter(int i, int digit, bool note = false, bool autoClean = true, bool hint = false)
    {
        if (!Editable(i) || digit is < 0 or > 9 || hint && (digit != Puzzle.Solution[i] || note || RulesVersion > 0 && HintCredits == 0)) return false;
        if (note && digit != 0)
        {
            if (Values[i] != 0) return false;
            Remember(); Notes[i] ^= 1 << digit;
            return true;
        }
        if (Values[i] == digit && Notes[i] == 0) return false;
        Remember();
        Values[i] = digit; Notes[i] = 0;
        if (hint) { Hints++; if (RulesVersion > 0) HintCredits--; }
        else if (digit != 0 && digit != Puzzle.Solution[i])
        { Mistakes++; if (RulesVersion > 0) Lives = Math.Max(0, Lives - 1);
            if (RulesVersion >= 3 && OutOfLives && !ReviveAttempted) ReviveDeadline = DateTimeOffset.UtcNow.AddSeconds(10); }
        if (autoClean && digit != 0 && digit == Puzzle.Solution[i])
            foreach (int peer in Sudoku.Peers[i]) Notes[peer] &= ~(1 << digit);
        TryAutoFinish(); return true;
    }

    public int CandidateDelta()
    {
        int changes = 0;
        for (int i = 0; i < 81; i++)
            changes += BitOperations.PopCount((uint)(Notes[i] ^ (Values[i] == 0 ? Sudoku.Candidates(Values, i) : 0)));
        return changes;
    }

    public bool FillNotes()
    {
        if (Paused || Completed || OutOfLives) return false;
        var next = Enumerable.Range(0, 81).Select(i => Values[i] == 0 ? Sudoku.Candidates(Values, i) : 0).ToArray();
        if (next.SequenceEqual(Notes) || RulesVersion > 0 && CandidateCredits == 0) return false;
        int changes = CandidateDelta();
        Remember(); Notes = next;
        if (RulesVersion > 0) { CandidateCredits--; CandidateUses++; }
        if (RulesVersion >= 2) { CandidateChanges += changes; CandidatePenalty += (long)CandidatePointCost * changes; }
        return true;
    }

    public (int Cell, int Digit, string Reason)? GetHint()
    {
        if (Paused || Completed || OutOfLives || RulesVersion > 0 && HintCredits == 0) return null;
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
        Paused = NotesMode = Awarded = false; Selected = Array.IndexOf(Values, 0);
        RulesVersion = 2; Lives = MaxLives; HintCredits = CandidateCredits = 1;
        CandidateUses = LifeRestores = AdsWatched = 0; CandidateChanges = CandidatePenalty = 0; AutoFilled = 0; ReviveDeadline = null; ReviveAttempted = false; CancelRevive(); ManualCorrect.Clear();
    }

    public bool Valid()
    {
        if (Puzzle is null || Puzzle.Givens is null || Puzzle.Solution is null ||
            !Enum.IsDefined(Puzzle.Difficulty) || !Sudoku.IsConsistent(Puzzle.Givens) ||
            !Sudoku.IsConsistent(Puzzle.Solution) || Puzzle.Solution.Contains(0) ||
            RulesVersion is < 0 or > 3 || AutoFilled is < 0 or > 9 || Lives is < 0 or > MaxLives ||
            HintCredits is < 0 or > MaxAid || CandidateCredits is < 0 or > MaxAid ||
            CandidateUses < 0 || CandidateChanges is < 0 or > 1_000_000_000 || CandidatePenalty is < 0 or > 10_000_000_000 || LifeRestores < 0 || AdsWatched < 0 || ManualCorrect is null ||
            ManualCorrect.Any(i => i is < 0 or >= 81 || Puzzle.Givens[i] != 0) ||
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
