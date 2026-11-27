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
    private void OpenMenu()
    {
        if (Busy || watchingReward)return;
        if (Playing)
        {
            ReadClock();
            G!.Paused = true;
        }
        inMenu = true;
        SyncClock();
        Render();
        Save();
    }
    private void Render()
    {
        if (localPaintOption is { } option && option.IsChecked != state.PaintDigit)
        {
            updatingPaintOption = true;
            try { option.IsChecked = state.PaintDigit; }
            finally { updatingPaintOption = false; }
        }
        var g = G;
        bool active = Playing, visible = g is not null && !g.Paused && !g.OutOfLives && !g.Completed && !Busy;
        heading.Text = g is null ? "Підготовка" : g.Completed ? "Розв’язано" : Level(g.Puzzle.Difficulty);
        errors.Text = !state.ShowMistakes ? "Приховано" : (g?.Mistakes ?? 0).ToString(CultureInfo.CurrentCulture);
        ReadClock();
        for (int i = 0; i < levels.Length; i++)
        {
            bool chosen = (int)nextDifficulty == i;
            levels[i].Background = B(chosen ? "Accent" : "Control");
            levels[i].Foreground = B(chosen ? "AccentText" : "Text");
            levels[i].FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal;
            levels[i].IsEnabled = !Busy;
        }
        int selectedValue = g is null ? 0 : state.PaintDigit && ActivePaintDigit != 0 ? ActivePaintDigit :
            DigitOnly ? localFocusDigit : g.Values[g.Selected];
        for (int i = 0; i < 81; i++)
        {
            var b = cells[i];
            b.IsTabStop = active && !DigitOnly && i == g!.Selected;
            if (g is null)
            {
                b.Content = null;
                b.Background = B("Panel");
                continue;
            }
            int value = g.Values[i];
            DrawGameCell(b, i, value, g.Notes[i], g.Puzzle.Givens[i] != 0, !DigitOnly && i == g.Selected,
                value != 0 && value != g.Puzzle.Solution[i],
                selectedValue != 0 && selectedValue == value, !DigitOnly && state.HighlightPeers && Sudoku.Peers[g.Selected].Contains(i), selectedValue);
        }
        for (int i = 0; i < 9; i++)
        {
            int d = i + 1;
            int remaining = g is null ? 9 : g.Remaining(d);
            DrawGameDigit(digits[i], d, remaining, active, d == selectedValue);
        }
        int filled = g?.Values.Count(v => v != 0) ?? 0;
        int givens = g?.Puzzle.Givens.Count(v => v != 0) ?? 0;
        SetCompactMetrics(progress, g is null ? [("Стан", "Підготовка поля")] :
            [("Заповнено", $"{filled} / 81"), ("Ваші клітинки", $"{filled - givens} / {81 - givens}"), ("Підказки", g.Hints.ToString())]);
        progressBar.Value = filled;
        undo.IsEnabled = active && g!.UndoStack.Count > 0;
        redo.IsEnabled = active && g!.RedoStack.Count > 0;
        erase.IsEnabled = active && !DigitOnly && g!.Editable(g.Selected);
        hint.IsEnabled = fill.IsEnabled = notes.IsEnabled = active;
        hint.Content = $"Підказка ({g?.HintCredits ?? 0})";
        fill.Content = $"Кандидати ({g?.CandidateCredits ?? 0})";
        fill.ToolTip = g is null ? null : $"Зміниться {g.CandidateDelta()} кандидатів\nШтраф: −{(long)Game.CandidatePointCost * g.CandidateDelta()} балів";
        quest.Text = account is null ? "Щоденні нагороди доступні в акаунті." : daily is null ? "Щоденні нагороди доступні онлайн" : $"Сьогодні: {daily.Wins}/7 перемог\nНаборів: {daily.Packs.Count}";
        rewardLife.IsEnabled = false;
        autoFinish.IsEnabled = !Busy && g is { Completed: false, OutOfLives: false };
        rewardHint.Visibility = g?.CanAdReward("hint") == true && WalletCount("hint") == 0 ? Visibility.Visible : Visibility.Collapsed;
        rewardCandidates.Visibility = g?.CanAdReward("candidates") == true && WalletCount("candidates") == 0 ? Visibility.Visible : Visibility.Collapsed;
        rewardHint.IsEnabled = rewardCandidates.IsEnabled = !Busy && !watchingReward;
        resultDaily.Visibility = account is not null && g?.Completed == true && daily?.Packs.Any(p => p.Milestone is not ("weekly" or "challenge")) == true ? Visibility.Visible : Visibility.Collapsed;
        notes.Content = g?.NotesMode == true ? "Нотатки: увімк." : "Нотатки";
        notes.Background = B(g?.NotesMode == true ? "Same" : "Control");
        pause.IsEnabled = g is not null && !Busy && !g.Completed && !g.OutOfLives;
        pause.Content = g?.Paused == true ? "Продовжити" : "Пауза";
        restart.IsEnabled = g is not null && !Busy && state.PendingReward is null;
        newGame.IsEnabled = !Busy && state.PendingReward is null;
        theme.Content = state.Dark ? "☀  Світла тема" : "☾  Темна тема";
        board.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        cover.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        coverTitle.Text = Busy ? "Генеруємо поле…" : g?.Completed == true ? "Судоку розв’язано!" :
            g?.OutOfLives == true ? "Поразка" : g?.Paused == true ? "Гра на паузі" : "Почнімо гру";
        coverDetail.Text = Busy ? "Перевіряємо єдиність розв’язку." : g?.Completed == true ?
            $"Час: {FormatTime(g.Seconds)}\nПомилки: {g.Mistakes}\nПідказки: {g.Hints}" :
            g?.OutOfLives == true ? "Останній шанс відновити життя." :
            g?.Paused == true ? "Поле приховано, таймер зупинено." : "Оберіть складність і створіть головоломку.";
        coverButton.Content = Busy ? "Скасувати" : g?.OutOfLives == true ? "Відновити життя за рекламу" : g?.Paused == true ? "Продовжити" : "Нова гра";
        coverButton.IsEnabled = true;
        if (g?.OutOfLives == true && !Busy) UpdateDefeat();
        bool showResult = !inMenu && !Busy && !watchingReward && g is not null && (g.Completed || g.FinalDefeat(DateTimeOffset.UtcNow));
        menuCard.Visibility = inMenu ? Visibility.Visible : Visibility.Collapsed;
        continueMenu.Visibility = G is null ? Visibility.Collapsed : Visibility.Visible;
        continueMenu.Content = G is { Completed: true } || G?.FinalDefeat(DateTimeOffset.UtcNow) == true ? "Останній результат" : "Продовжити гру";
        continueMenu.IsEnabled = !Busy && state.PendingReward is null;
        playCard.Visibility = sideCard.Visibility = inMenu || showResult ? Visibility.Collapsed : Visibility.Visible;
        resultCard.Visibility = showResult ? Visibility.Visible : Visibility.Collapsed;
        if (showResult && shownResult != state.GameId)
        {
            shownResult = state.GameId;
            _ = ShowResult(g!);
        }
        UpdateConnection();
        UpdateHomeCue();
    }

    private void UpdateDefeat()
    {
        if (G is not { OutOfLives: true } g || Busy) return;
        int left = g.ReviveSeconds(DateTimeOffset.UtcNow);
        coverDetail.Text = g.ReviveReadyAt is not null ? "Останній шанс використано. Очікуємо рекламу." :
            left > 0 ? $"Останній шанс: {left} с" : "Час вичерпано.";
        coverButton.IsEnabled = left > 0 && !watchingReward;
    }

    private async Task ShowResult(Game g)
    {
        bool win = g.Completed;
        resultTitle.Text = win ? "Головоломку розв’язано!" : "Поразка";
        SetCompactMetrics(resultSummary, [("Складність", Level(g.Puzzle.Difficulty)), ("Час", FormatTime(g.Seconds)),
            ("Бали", (win ? g.CurrentScore : 0).ToString("N0"))]);
        SetDetailRows(resultBreakdown, ("База", g.BaseScore.ToString("N0")), ("Бонус швидкості", $"+{g.SpeedBonus:N0}"),
            ("Час", $"−{g.Seconds / 3}"), ($"Помилки ({g.Mistakes})", $"−{75L * g.Mistakes}"),
            ($"Підказки ({g.Hints})", $"−{150L * g.Hints}"), ($"Кандидати ({g.CandidateChanges})", $"−{g.EffectiveCandidatePenalty}"),
            ($"Автозавершення ({g.AutoFilled})", $"−{g.AutoFillPenalty}"), ("Відновлення життя", $"−{100L * g.LifeRestores}"));
        if (win) SetDetailLoading(resultComparison, "Завантаження статистики…");
        else SetDetailMessage(resultComparison, "Порівняння доступне для завершених ігор.");
        Fade(resultCard);
        if (!win) return;
        var id = state.GameId;
        try
        {
            var c = await Request<DifficultyComparison > ($"comparison?difficulty={g.Puzzle.Difficulty}&score={g.CurrentScore}&seconds={g.Seconds}&rules={g.RulesVersion}", token: "");
            if (closing || id != state.GameId) return;
            if (c.Games == 0) SetDetailMessage(resultComparison, "Поки недостатньо результатів.");
            else SetDetailRows(resultComparison, ("Ігор", c.Games.ToString()), ("Гравців", c.Players.ToString()),
                ("Середній час", FormatTime((int)c.AverageSeconds)), ("Медіана", FormatTime((int)c.MedianSeconds)),
                ("Найкращий час", FormatTime(c.BestSeconds)), ("Середній бал", c.AverageScore.ToString("N0")),
                ("Середні помилки", c.AverageMistakes.ToString("F1")), ("Середні підказки", c.AverageHints.ToString("F1")), ("Середні зміни кандидатів", c.AverageCandidates.ToString("F1")),
                ("Ваш бал не нижче ніж у", $"{c.ScorePercentile}% ігор"), ("Ваш час не довше ніж у", $"{c.TimePercentile}% ігор"));
        }
        catch
        {
            if (!closing && id == state.GameId)
            {
                SetDetailMessage(resultComparison, "Статистика тимчасово недоступна.");
                var retry = Action("↻", () => _ = ShowResult(g), "Повторити завантаження статистики");
                retry.HorizontalAlignment = HorizontalAlignment.Left;
                retry.MinWidth = 36;
                AutomationProperties.SetName(retry, "Повторити завантаження статистики");
                resultComparison.Children.Add(retry);
            }
        }
    }

    private static void Fade(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(220)));
    }
    private static void Shake(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, x) in new[] { (0, 0), (60, - 3), (120, 3), (180, - 2), (240, 0) })
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private sealed class CellVisual
    {
        public int Value = -1, Mask = -1, Focus = -1;
        public string? Name;
    }
    private sealed record DigitVisual(TextBlock Count);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, CellVisual> cellVisuals = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, DigitVisual> digitVisuals = new();

    private void DrawGameCell(Button b, int i, int value, int mask, bool given, bool selected, bool wrong, bool same, bool peer, int focusDigit)
    {
        b.Background = B(selected ? "Selected" : wrong ? "WrongBg" : same ? "Same" : peer ? "Peer" : "Panel");
        b.Foreground = B(wrong ? "Wrong" : given ? "Given" : "Accent");
        b.FontWeight = given ? FontWeights.SemiBold : FontWeights.Normal;
        var visual = cellVisuals.GetOrCreateValue(b);
        int effectiveFocus = value == 0 && (mask & (1 << focusDigit)) != 0 ? focusDigit : 0;
        if (visual.Value != value || visual.Mask != mask || visual.Focus != effectiveFocus ||
            (b.Content is null && (value != 0 || mask != 0)))
        {
            b.Content = value != 0 ? value.ToString() : mask != 0 ? NoteGrid(mask, effectiveFocus) : null;
            visual.Value = value; visual.Mask = mask; visual.Focus = effectiveFocus;
        }
        string name = $"Рядок {i / 9 + 1}, стовпець {i % 9 + 1}, " +
            (value == 0 ? "порожньо" : value.ToString()) + (given ? ", задано" : "") +
            (wrong ? ", помилка" : "") + (selected ? ", вибрано" : "");
        if (visual.Name != name) { AutomationProperties.SetName(b, name); visual.Name = name; }
    }

    private void DrawGameDigit(Button button, int digit, int remaining, bool enabled, bool focused)
    {
        if (!digitVisuals.TryGetValue(button, out var visual))
        {
        var grid = new Grid { MinHeight = 35 };
        var number = Text(digit.ToString(), 21, "Text", true);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        var count = Text("", 10, "Muted");
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(number);
        grid.Children.Add(count);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Content = grid;
        visual = new DigitVisual(count);
        digitVisuals.Add(button, visual);
        }
        string remainingText = remaining.ToString();
        if (visual.Count.Text != remainingText)
        {
            visual.Count.Text = remainingText;
            AutomationProperties.SetName(button, $"Цифра {digit}, залишилось {remaining}");
        }
        button.IsEnabled = enabled && remaining > 0;
        button.Background = B(focused && remaining > 0 ? "Same" : "Control");
        button.Opacity = remaining == 0 ? 0.55 : 1;
    }

    private UniformGrid NoteGrid(int mask, int focusDigit)
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 51, Height = 51 };
        for (int n = 1; n <= 9; n++)
        {
            bool marked = n == focusDigit && (mask & (1 << n)) != 0;
            var t = Text((mask & (1 << n)) != 0 ? n.ToString() : "", 13, marked ? "AccentText" : "Muted", marked);
            t.TextAlignment = TextAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            if (marked)
            {
                var highlight = new Border { Child = t, CornerRadius = new CornerRadius(3), Margin = new Thickness(1) };
                highlight.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                grid.Children.Add(highlight);
            }
            else grid.Children.Add(t);
        }
        return grid;
    }

    private static string FormatTime(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
