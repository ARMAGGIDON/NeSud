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
    private void TogglePause()
    {
        if (G is null || G.Completed || G.OutOfLives || Busy || state.PendingReward is not null) return;
        ReadClock();
        G.Paused = !G.Paused;
        SyncClock();
        if (Playing && G.AutoFinish) Change(g => g.TryAutoFinish());
        Render();
        Save();
        FocusCell();
    }
    private void Restart()
    {
        if (G is null || Busy || watchingReward || state.PendingReward is not null) return;
        if (!Confirm("Очистити всі ходи, нотатки та час цієї гри?")) return;
        ReadClock();
        var previous = Terminal(G.Completed ? "completed" : G.OutOfLives ? "defeated" : "abandoned");
        var scope = state.GameScope ?? "";
        clock.Reset();
        state.Game = Game.Start(G.Puzzle);
        state.Game.AutoFinish = state.AutoFinish;
        BeginAttempt();
        lastSavedSecond = 0;
        SyncClock();
        Render();
        Save(previous, scope);
        FocusCell();
        status.Text = "";
    }
    private bool Confirm(string message) => Dialog("Підтвердження", message, true);
    private async Task Generate(bool confirm)
    {
        if (Busy || closing || watchingReward || state.PendingReward is not null) return;
        if (confirm && G is { Completed: false, OutOfLives: false } g && (g.UndoStack.Count > 0 || g.Seconds > 0) &&
            !Confirm("Поточну незавершену гру буде замінено. Створити нову?")) return;
        inMenu = false;
        ReadClock();
        clock.Reset();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        generation = cts;
        var difficulty = nextDifficulty;
        Render();
        try
        {
            var puzzle = await Task.Run(() => Sudoku.Generate(difficulty, cts.Token), cts.Token);
            if (closing) return;
            var previous = G is null ? null : Terminal(G.Completed ? "completed" : G.OutOfLives ? "defeated" : "abandoned");
            string scope = state.GameScope ?? "";
            state.Game = Game.Start(puzzle);
            state.Game.AutoFinish = state.AutoFinish;
            BeginAttempt();
            lastSavedSecond = 0;
            Save(previous, scope);
            status.Text = "";
        }
        catch (OperationCanceledException)
        { if (!closing) status.Text = "Генерацію скасовано або перевищено час очікування."; }
        catch (Exception ex)
        {
            if (!closing) status.Text = ex.Message;
        }
        finally
        {
            generation = null;
            if (!closing)
            {
                SyncClock();
                Render();
                Save();
                FocusCell();
            }
        }
    }


    private void BeginAttempt()
    {
        localFocusDigit = localPaintDigit = 0;
        state.GameId = Guid.NewGuid();
        state.Started = DateTimeOffset.UtcNow;
        state.GameScope = Scope;
        defeatSaved = false;
        shownResult = Guid.Empty;
    }

    private GameRecord Terminal(string outcome) => new(state.GameId, state.Started, DateTimeOffset.UtcNow,
        "desktop", outcome, new Game { Puzzle = G!.Puzzle, Values = (int[])G.Values.Clone(), Notes = (int[])G.Notes.Clone(),
            Seconds = G.Seconds, Mistakes = G.Mistakes, Hints = G.Hints, Selected = G.Selected,
            NotesMode = G.NotesMode, Paused = G.Paused, Awarded = G.Awarded, RulesVersion = G.RulesVersion,
            Lives = G.Lives, HintCredits = G.HintCredits, CandidateCredits = G.CandidateCredits, CandidateUses = G.CandidateUses, CandidatePenalty = G.CandidatePenalty, CandidateChanges = G.CandidateChanges,
            LifeRestores = G.LifeRestores, AutoFinish = G.AutoFinish, AutoFilled = G.AutoFilled, ReviveAttempted = G.ReviveAttempted,
            ReviveDeadline = G.ReviveDeadline, ReviveReadyAt = G.ReviveReadyAt, ReviveExpiresAt = G.ReviveExpiresAt, AdsWatched = G.AdsWatched, ManualCorrect = new HashSet<int > (G.ManualCorrect) });
}
