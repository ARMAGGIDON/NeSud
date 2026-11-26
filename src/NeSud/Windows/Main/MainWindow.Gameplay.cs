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
            if (delta > 0) { g.Seconds += delta; accountedSeconds = elapsed; }
        }
        time.Text = FormatTime(G?.Seconds ?? 0);
        lives.Text = G is null ? "♡ ♡ ♡" : string.Join(" ", Enumerable.Range(0, Game.MaxLives).Select(i => i < G.Lives ? "♥" : "♡"));
        score.Text = G is null ? "0" : G.CurrentScore.ToString("N0");
        lives.ToolTip = G is null ? null : $"Життя: {G.Lives}/{Game.MaxLives} • Помилки: {G.Mistakes} • Відновлено: {G.LifeRestores}";
        score.ToolTip = G is null ? null : $"Бонус: +{G.SpeedBonus} • Автозавершення: −{G.AutoFillPenalty} • Час: −{G.Seconds / 3} • Помилки: −{75L * G.Mistakes} • Підказки: −{150L * G.Hints} • Кандидати: −{G.EffectiveCandidatePenalty} • Відновлення: −{100L * G.LifeRestores}";
    }
    private void SyncClock()
    {
        ReadClock();
        if (Playing && !clock.IsRunning) { accountedSeconds = 0; clock.Restart(); }
        else if (!Playing) { clock.Reset(); accountedSeconds = 0; }
    }
    private void Save(GameRecord? previous = null, string? previousScope = null)
    {
        try
        {
            store.Save(state, previous ?? (G is { Completed: true } ? Terminal("completed") : G?.FinalDefeat(DateTimeOffset.UtcNow) == true ? Terminal("defeated") : null), previousScope ?? state.GameScope ?? ""); lastSavedSecond = G?.Seconds ?? 0;
            if (previous is not null || G is { Completed: true } && !syncing && nextSync > DateTimeOffset.UtcNow.AddSeconds(10))
                nextSync = DateTimeOffset.UtcNow.AddSeconds(10);
            saveStatus.Text = ""; saveStatus.Foreground = B("Muted"); saveStatus.ToolTip = LocalStore.DefaultPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { saveStatus.Text = "Не збережено"; saveStatus.Foreground = B("Wrong"); saveStatus.ToolTip = ex.Message; }
    }
    private void FocusCell() { if (Playing) cells[G!.Selected].Focus(); }
    private void Select(int i, bool focus = true)
    {
        if (!Playing) return;
        G!.Selected = i; Render(); Save(); if (focus) FocusCell();
    }
    private void Change(Func<Game, bool> action, string? message = null)
    {
        if (!Playing) return;
        ReadClock(); var before = (int[])G!.Values.Clone(); int oldLives = G.Lives;
        if (!action(G)) return;
        if (G!.OutOfLives) { SyncClock(); status.Text = "Життя закінчилися. Відновіть життя або створіть нову гру."; }
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
        Render(); Save(); FocusCell();
        if (oldLives > G.Lives) Fade(lives);
        for (int i = 0; i < 81; i++) if (before[i] != G.Values[i] && G.Values[i] != 0)
        { if (G.Values[i] == G.Puzzle.Solution[i]) Fade(cells[i]); else Shake(cells[i]); }
    }
    private void Enter(int d, bool temporaryNote = false) => Change(g =>
        g.Enter(g.Selected, d, temporaryNote || g.NotesMode, state.AutoClean));
    private void ToggleNotes()
    {
        if (!Playing) return;
        G!.NotesMode = !G.NotesMode; status.Text = "";
        Render(); Save(); FocusCell();
    }
    private void Hint()
    {
        if (!Playing) return;
        if (G!.HintCredits == 0) { _ = ChooseResource("hint"); return; }
        if (G.GetHint() is not { } m) return;
        G.Selected = m.Cell;
        Change(g => g.Enter(m.Cell, m.Digit, false, state.AutoClean, true),
            $"{m.Reason}: рядок {m.Cell / 9 + 1}, стовпець {m.Cell % 9 + 1} = {m.Digit}.");
    }
    private void Candidates()
    {
        if (!Playing) return;
        if (G!.CandidateCredits == 0) { _ = ChooseResource("candidates"); return; }
        int delta = G.CandidateDelta();
        if (delta == 0) return;
        Change(g => g.FillNotes(), $"Кандидати: {delta} змін • −{delta * Game.CandidatePointCost} балів.");
    }
}
