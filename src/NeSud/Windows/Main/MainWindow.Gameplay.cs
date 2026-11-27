using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NeSud.Core;
using NeSud.Storage;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

namespace NeSud;

public sealed partial class MainWindow : Window
{
    private void ReadClock()
    {
        if (G is { } g && clock.IsRunning)
        {
            int elapsed = (int)clock.Elapsed.TotalSeconds;
            int delta = elapsed - accountedSeconds;
            if (delta > 0)
            {
                g.Seconds += delta;
                accountedSeconds = elapsed;
            }
        }
        time.Text = FormatTime(G?.Seconds ?? 0);
        lives.Text = G is null ? "♡ ♡ ♡" : string.Join(" ", Enumerable.Range(0, Game.MaxLives).Select(i => i < G.Lives ? "♥" : "♡"));
        score.Text = G is null ? "0" : G.CurrentScore.ToString("N0");
        lives.ToolTip = G is null ? null : $"Життя: {G.Lives}/{Game.MaxLives}\nПомилки: {G.Mistakes}\nВідновлено: {G.LifeRestores}";
        score.ToolTip = G is null ? null : $"Бонус: +{G.SpeedBonus}\nАвтозавершення: −{G.AutoFillPenalty}\nЧас: −{G.Seconds / 3}\nПомилки: −{75L * G.Mistakes}\nПідказки: −{150L * G.Hints}\nКандидати: −{G.EffectiveCandidatePenalty}\nВідновлення: −{100L * G.LifeRestores}";
    }
    private void SyncClock()
    {
        ReadClock();
        if (Playing && !clock.IsRunning)
        {
            accountedSeconds = 0;
            clock.Restart();
        }
        else if (!Playing)
        {
            clock.Reset();
            accountedSeconds = 0;
        }
    }
    private Task<Exception?>? periodicWrite;
    private long saveEpoch;

    private async void SavePeriodic()
    {
        if (closing || periodicWrite is { IsCompleted: false }) return;
        string snapshot = Database.Encode(state);
        int seconds = G?.Seconds ?? 0;
        Guid id = state.GameId;
        long epoch = ++saveEpoch;
        lastSavedSecond = seconds;
        var task = Task.Run<Exception?>(() =>
        {
            try { store.SaveSnapshot(snapshot); return null; }
            catch (Exception ex) { return ex; }
        });
        periodicWrite = task;
        var error = await task;
        if (closing || saveEpoch != epoch || state.GameId != id) return;
        if (error is not null)
        {
            lastSavedSecond = seconds - 5;
            saveStatus.Text = "Не збережено";
            saveStatus.Foreground = B("Wrong");
            saveStatus.ToolTip = error.Message;
        }
        else
        {
            saveStatus.Text = "";
            saveStatus.Foreground = B("Muted");
            saveStatus.ToolTip = LocalStore.DefaultPath;
        }
    }

    private void Save(GameRecord? previous = null, string? previousScope = null)
    {
        try
        {
            ++saveEpoch;
            // Explicit saves must follow any earlier immutable autosave snapshot.
            periodicWrite?.GetAwaiter().GetResult();
            store.Save(state, previous ?? (G is { Completed: true } ? Terminal("completed") : G?.FinalDefeat(DateTimeOffset.UtcNow) == true ? Terminal("defeated") : null), previousScope ?? state.GameScope ?? "");
            lastSavedSecond = G?.Seconds ?? 0;
            if (previous is not null || G is { Completed: true } && !syncing && nextSync > DateTimeOffset.UtcNow.AddSeconds(10))
                nextSync = DateTimeOffset.UtcNow.AddSeconds(10);
            saveStatus.Text = "";
            saveStatus.Foreground = B("Muted");
            saveStatus.ToolTip = LocalStore.DefaultPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { saveStatus.Text = "Не збережено";
        saveStatus.Foreground = B("Wrong");
        saveStatus.ToolTip = ex.Message; }
    }
    private int localFocusDigit, localPaintDigit;
    private Guid paintGameId;
    private int ActivePaintDigit => paintGameId == state.GameId && localPaintDigit != 0
        ? localPaintDigit : DigitOnly ? localFocusDigit : G?.Values[G.Selected] ?? 0;
    private void SetPaintDigit(int digit)
    {
        if (digit is < 1 or > 9) return;
        paintGameId = state.GameId;
        localPaintDigit = digit;
    }

    private void ClickCell(int i)
    {
        if (!Playing || G is not { } g) return;
        int digit = ActivePaintDigit;
        bool paint = state.PaintDigit && digit != 0 && g.Remaining(digit) > 0 && g.Values[i] == 0 && g.Editable(i);
        if (paint) SetPaintDigit(digit);
        if (!paint) { Select(i); return; }
        PaintLocalCell(i, digit, g.NotesMode || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private Guid focusGameId;
    private bool DigitOnly => focusGameId == state.GameId && localFocusDigit != 0;

    private void FocusCell()
    {
        if (Playing && !DigitOnly) cells[G!.Selected].Focus();
    }
    private void Select(int i, bool focus = true)
    {
        if (!Playing) return;
        localFocusDigit = 0;
        G!.Selected = i;
        if (state.PaintDigit) SetPaintDigit(G.Values[i]);
        Render();
        // Selection is UI state; the periodic save and pause/close persist it.
        if (focus) FocusCell();
    }

    private void PaintLocalCell(int i, int digit, bool note)
    {
        var g = G!;
        localFocusDigit = 0;
        g.Selected = i;
        SetPaintDigit(digit);
        if (note && (g.Notes[i] & (1 << digit)) == 0 && (Sudoku.Candidates(g.Values, i) & (1 << digit)) == 0)
        {
            Render();
            FocusCell();
            return;
        }
        Change(x => x.Enter(i, digit, note, state.AutoClean));
    }

    private void Change(Func<Game, bool> action, string? message = null)
    {
        if (!Playing) return;
        ReadClock();
        var before = (int[])G!.Values.Clone();
        int oldLives = G.Lives;
        var remainingBefore = Enumerable.Range(1, 9).Select(G.Remaining).ToArray();
        int anchor = G.Selected;
        if (!action(G)) return;
        if (!G.Completed && !G.OutOfLives)
        {
            int digit = G.Values[anchor];
            if (digit != 0 && before[anchor] != digit && remainingBefore[digit - 1] > 0 && G.Remaining(digit) == 0)
                AdvanceDigit(digit, anchor);
        }
        if (G!.OutOfLives)
        {
            SyncClock();
            status.Text = "Життя закінчилися. Відновіть життя або створіть нову гру.";
        }
        else if (G.Completed)
        {
            SyncClock();
            if (!G.Awarded)
            {
                if (!state.Stats.TryGetValue(G.Puzzle.Difficulty, out var s))
                    state.Stats[G.Puzzle.Difficulty] = s = new Statistics();
                s.Wins++;
                if (G.Hints == 0 && G.Mistakes == 0 && G.CandidateUses == 0 && G.LifeRestores == 0 && G.AutoFilled == 0)
                    s.BestSeconds = Math.Min(s.BestSeconds ?? int.MaxValue, G.Seconds);
                G.Awarded = true;
            }
            status.Text = $"Головоломку завершено! {G.CurrentScore:N0} балів.";
        }
        else status.Text = "";
        Render();
        // Avoid a FULL synchronous SQLite transaction for each mouse sample.
        // Terminal results remain immediately durable; the existing five-second autosave handles active games.
        if (G.Completed || G.OutOfLives) Save();
        if (G.Completed && account is not null && (state.GameScope ?? "") == Scope)
        {
            pendingWinReward = state.GameId;
            pendingWinBaseline = daily;
            nextSync = DateTimeOffset.MinValue;
            _ = Sync();
        }
        FocusCell();
        if (oldLives > G.Lives) Fade(lives);
        for (int i = 0; i < 81; i++) if (before[i] != G.Values[i] && G.Values[i] != 0)
        { if (G.Values[i] == G.Puzzle.Solution[i]) Fade(cells[i]);
        else Shake(cells[i]); }
    }
    private void AdvanceDigit(int digit, int anchor)
    {
        var g = G!;
        int next = BoardFocus.NextDigit(digit, Enumerable.Range(1, 9).Select(g.Remaining).ToArray());
        if (next == 0) return;
        int cell = BoardFocus.Nearest(g.Values, anchor, next, false);
        localFocusDigit = cell < 0 ? next : 0;
        focusGameId = state.GameId;
        g.Selected = cell < 0 ? anchor : cell;
        if (state.PaintDigit) SetPaintDigit(next);
        if (cell < 0) digits[next - 1].Focus();
    }

    private void Enter(int d, bool temporaryNote = false)
    {
        if (!Playing || G is not { } g) return;
        if (d > 0 && g.Remaining(d) == 0) return;
        if (state.PaintDigit && d > 0) SetPaintDigit(d);
        if (DigitOnly || !g.Editable(g.Selected))
        {
            if (d == 0) return;
            int next = BoardFocus.Nearest(g.Values, g.Selected, d, !DigitOnly);
            if (next >= 0) Select(next);
            else
            {
                focusGameId = state.GameId;
                localFocusDigit = d;
                // Keep keyboard input on the pad rather than the old cell.
                digits[d - 1].Focus();
                Render();
            }
            return;
        }
        if (state.PaintDigit && d > 0)
            PaintLocalCell(g.Selected, d, temporaryNote || g.NotesMode);
        else Change(x => x.Enter(x.Selected, d, temporaryNote || x.NotesMode, state.AutoClean));
    }
    private void ToggleNotes()
    {
        if (!Playing) return;
        G!.NotesMode = !G.NotesMode;
        status.Text = "";
        Render();
        Save();
        FocusCell();
    }
    private void Hint()
    {
        if (!Playing) return;
        if (G!.HintCredits == 0)
        {
            _ = ChooseResource("hint");
            return;
        }
        if (G.GetHint() is not { } m) return;
        localFocusDigit = 0;
        if (state.PaintDigit) SetPaintDigit(m.Digit);
        G.Selected = m.Cell;
        Change(g => g.Enter(m.Cell, m.Digit, false, state.AutoClean, true),
            $"{m.Reason}: рядок {m.Cell / 9 + 1}, стовпець {m.Cell % 9 + 1} = {m.Digit}.");
    }
    private void Candidates()
    {
        if (!Playing) return;
        if (G!.CandidateCredits == 0)
        {
            _ = ChooseResource("candidates");
            return;
        }
        int delta = G.CandidateDelta();
        if (delta == 0) return;
        Change(g => g.FillNotes(), $"Змін кандидатів: {delta}. Штраф: −{delta * Game.CandidatePointCost} балів.");
    }
}
