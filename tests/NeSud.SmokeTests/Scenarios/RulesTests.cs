using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestRules(List<Puzzle> puzzles)
    {
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
        Check(lifeGame.ReviveDeadline is null && !lifeGame.FinalDefeat(lossTime.AddHours(1)) &&
            !lifeGame.CanStartRevive(lossTime.AddHours(1)), "Successful revival clears deadline; elapsed time alone cannot defeat");
        lifeGame.Seconds += 3600;
        Check(lifeGame.Enter(blanks[3], lifeGame.Puzzle.Solution[blanks[3]]) &&
            lifeGame.Lives == 1 && !lifeGame.FinalDefeat(lossTime.AddHours(1)), "Correct move after revival preserves life");
        for (int n = 2; n <= 4; n++)
        {
            int cell = blanks[n + 2];
            Check(lifeGame.Enter(cell, lifeGame.Puzzle.Solution[cell] % 9 + 1) &&
                lifeGame.Mistakes == n + 2 && lifeGame.Lives == 0, "Life exhaustion after revival");
            var lost = lifeGame.ReviveDeadline!.Value.AddSeconds(-10);
            Check(!lifeGame.FinalDefeat(lost) && lifeGame.CanStartRevive(lost) &&
                lifeGame.NextReviveCost == 100L * n, "New revival window and increasing price");
            Check(lifeGame.BeginRevive(lost) && !lifeGame.FinishRevive(lost.AddSeconds(7)) &&
                lifeGame.FinishRevive(lost.AddSeconds(8)), "Repeated revival requires completed ad");
            Check(lifeGame.Lives == 1 && lifeGame.LifeRestores == n && lifeGame.LifeRestorePenalty == 50L * n * (n + 1) &&
                !lifeGame.FinalDefeat(lost.AddHours(1)), "Cumulative revival penalty");
        }
        var historical = Game.Start(puzzles[0]);
        historical.RulesVersion = 3;
        historical.LifeRestores = 3;
        Check(historical.LifeRestorePenalty == 300, "Historical scoring preserved");
        var activeLegacy = Game.Start(puzzles[0]);
        activeLegacy.RulesVersion = 3;
        activeLegacy.LifeRestores = 1;
        activeLegacy.ReviveAttempted = true;
        int oldScore = activeLegacy.CurrentScore;
        activeLegacy.UpgradeRules();
        Check(activeLegacy.RulesVersion == Game.CurrentRulesVersion && !activeLegacy.ReviveAttempted &&
            activeLegacy.CurrentScore == oldScore && activeLegacy.NextReviveCost == 200, "Active legacy revival migration");
        var aid = Game.Start(puzzles[0]);
        int blank = blanks[0], correctMask = Sudoku.Candidates(aid.Values, blank);
        int validDigit = BitOperations.TrailingZeroCount((uint) correctMask);
        int invalidDigit = BitOperations.TrailingZeroCount((uint)(Sudoku.All & ~correctMask));
        Check(aid.Enter(blank, validDigit, note: true), "Manual notes setup");
        int undoCount = aid.UndoStack.Count;
        Check(!aid.Enter(blank, invalidDigit, note: true) && aid.UndoStack.Count == undoCount &&
            aid.Mistakes == 0 && aid.Lives == Game.MaxLives, "Conflicting note rejected without side effects");
        aid.Notes[blank] |= 1 << invalidDigit; // Legacy/stale note may still be removed.
        Check(aid.Enter(blank, invalidDigit, note: true) && (aid.Notes[blank] & (1 << invalidDigit)) == 0,
            "Conflicting existing note can be erased");
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
        var copied = Database.Decode<Game > (Database.Encode(aid));
        Check(copied.Valid() && copied.CandidateUses == 1 && copied.CandidateChanges == expected &&
            copied.CurrentScore == aid.CurrentScore, "Resource persistence");
        var legacy = Game.Start(puzzles[0]);
        legacy.RulesVersion = 1;
        legacy.CandidateUses = 2;
        int legacyScore = legacy.CurrentScore;
        legacy.UpgradeRules();
        Check(legacy.RulesVersion == Game.CurrentRulesVersion && legacy.CurrentScore == legacyScore, "Legacy migration");
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
        foreach (int j in blanks.Take(blanks.Length - 10)) auto.Enter(j, auto.Puzzle.Solution[j]);
        auto.AutoFinish = true;
        Check(!auto.TryAutoFinish(), "Auto finish not at ten blanks");
        int tenth = blanks[blanks.Length - 10];
        Check(auto.Enter(tenth, auto.Puzzle.Solution[tenth]) && auto.Completed && auto.AutoFilled == 9 &&
            auto.AutoFillPenalty == 180 && auto.Seconds == 0, "Auto finish applies points not time");
        Check(auto.Remaining(1) == 0 && auto.Remaining(9) == 0, "Digit counts on completion");
        var manual = Game.Start(puzzles[0]);
        foreach (int j in blanks) manual.Enter(j, manual.Puzzle.Solution[j]);
        manual.Seconds = manual.TargetSeconds / 2;
        Check(manual.SpeedBonus == (int) Math.Round(manual.BaseScore * 0.6 * 0.25), "Quadratic midpoint");
        manual.Seconds = manual.TargetSeconds;
        Check(manual.SpeedBonus == 0, "Target speed bonus zero");
        var rapid = new MoveAudit();
        var nowAudit = DateTimeOffset.UtcNow;
        for (int j = 0; j < 20; j++) rapid = rapid.Record(nowAudit.AddMilliseconds(j * 30));
        Check(rapid.Flag(manual), "Private rapid move audit");
        Console.WriteLine("PASS timed single revival, cancellation, auto finish, counts, quadratic bonus and audit");
    }
}
