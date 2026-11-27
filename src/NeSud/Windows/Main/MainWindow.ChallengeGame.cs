using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private async Task ShowChallengeGame(string day, bool resumeOnly = false)
    {
        // The online session remains authoritative; only its presentation mirrors the local game.
        var root = new Grid();
        AddGameColumns(root);
        var play = BuildPlayGrid();
        var info = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 4; i++) info.ColumnDefinitions.Add(new ColumnDefinition());
        var level = Text("Підготовка", 19, "Text", true);
        var elapsed = Text("00:00", 19, "Text", true);
        var life = Text("♡ ♡ ♡", 19, "Wrong", true);
        var points = Text("0", 19, "Accent", true);
        At(info, Metric("СКЛАДНІСТЬ", level), 0, 0);
        At(info, Metric("ЧАС", elapsed), 0, 1);
        At(info, Metric("ЖИТТЯ", life), 0, 2);
        At(info, Metric("БАЛИ", points), 0, 3);
        At(play, info, 0, 0);

        var boardCells = new Button[81];
        Action<int>? selectCell = null;
        var grid = BuildBoard(boardCells, i => selectCell?.Invoke(i), i => selectCell?.Invoke(i));
        var boardArea = new Grid();
        boardArea.Children.Add(BoardView(grid));
        var cover = new Grid();
        cover.SetResourceReference(Panel.BackgroundProperty, "PanelBrush");
        var center = Stack();
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.MaxWidth = 320;
        var coverTitle = Text("", 27, "Text", true);
        coverTitle.TextAlignment = TextAlignment.Center;
        var notice = Text("", 15, "Muted");
        notice.TextAlignment = TextAlignment.Center;
        var mainAction = Action("Почати", primary: true);
        center.Children.Add(coverTitle);
        Gap(center);
        center.Children.Add(notice);
        Gap(center, 18);
        center.Children.Add(mainAction);
        cover.Children.Add(center);
        boardArea.Children.Add(cover);
        At(play, boardArea, 1, 0);
        var bottom = Stack();
        bottom.Margin = new Thickness(0, 14, 0, 0);
        var progress = new UniformGrid { Columns = 3 };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 81, Height = 5, Margin = new Thickness(0, 7, 0, 0), BorderThickness = new Thickness(0) };
        progressBar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        progressBar.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        bottom.Children.Add(progress);
        bottom.Children.Add(progressBar);
        At(play, bottom, 2, 0);
        At(root, Card(play), 0, 0);

        var side = Stack();
        side.Children.Add(Text("Щоденна гра", 19, "Text", true));
        side.Children.Add(Text(day, 14, "Muted"));
        Gap(side, 10);
        side.Children.Add(Text("Цифри та нотатки", 17, "Text", true));
        Gap(side, 5);
        var pad = new UniformGrid { Columns = 3 };
        side.Children.Add(pad);
        var padCells = new Button[9];
        Gap(side, 8);
        var tools = new UniformGrid { Columns = 2 };
        side.Children.Add(tools);
        Gap(side, 10);
        var settings = Stack();
        settings.Children.Add(Text("Налаштування", 16, "Text", true));
        side.Children.Add(settings);
        At(root, Card(new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 13), 0, 2);
        var window = GamePage("Щоденна гра", root);
        OnlineGameView? game = null;
        bool working = false, note = false, allowClose = false, connected = true;
        int selected = 0;
        var anchor = Stopwatch.StartNew();
        double reviveDeadline = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        bool Active() => connected && !working && game is { Paused: false, Completed: false, OutOfLives: false, Reward: null };
        void Select(int i)
        {
            if (!Active() || selected == i) return;
            selected = i;
            Draw();
        }
        for (int d = 1; d <= 9; d++)
        {
            var b = Action(d.ToString(), tip: $"Клавіша {d}");
            b.Margin = new Thickness(2);
            b.Padding = new Thickness(3, 4, 3, 4);
            b.MinHeight = 39;
            padCells[d - 1] = b;
            pad.Children.Add(b);
        }
        var undo = Action("↶  Скасувати", tip: "Ctrl+Z");
        var redo = Action("↷  Повторити", tip: "Ctrl+Y / Ctrl+Shift+Z");
        var erase = Action("Стерти", tip: "Delete / Backspace / 0");
        var notes = Action("Нотатки", tip: "N або Space; Shift+цифра для разової нотатки");
        var hint = Action("Підказка", tip: "H");
        var candidates = Action("Кандидати", tip: "A");
        var pause = Action("Пауза", tip: "P");
        foreach (var b in new[] { undo, redo, erase, notes, hint, candidates, pause })
        {
            b.Margin = new Thickness(2);
            b.MinHeight = 32;
            b.Padding = new Thickness(6, 4, 6, 4);
            tools.Children.Add(b);
        }
        var auto = new CheckBox { Content = Text("Автозавершення ≤9 клітинок"), IsChecked = state.AutoFinish };
        bool updatingOptions = false;
        auto.Checked += async (_, _) => await ChangeAuto(true);
        auto.Unchecked += async (_, _) => await ChangeAuto(false);
        settings.Children.Add(auto);
        settings.Children.Add(Option("Підсвічувати пов’язані клітинки", state.HighlightPeers, v => { state.HighlightPeers = v; Draw(); }));
        settings.Children.Add(Option("Автоматично очищати нотатки", state.AutoClean, v => state.AutoClean = v));
        async Task ChangeAuto(bool enabled)
        {
            if (updatingOptions) return;
            state.AutoFinish = enabled;
            Save();
            if (game is not null && game.AutoFinish != enabled) await Move("auto-finish");
        }
        selectCell = Select;
        for (int i = 0; i < padCells.Length; i++)
        {
            int digit = i + 1;
            padCells[i].Click += async (_, _) => await Move("enter", digit);
        }
        erase.Click += async (_, _) => await Move("enter", 0);
        undo.Click += async (_, _) => await Move("undo");
        redo.Click += async (_, _) => await Move("redo");
        notes.Click += (_, _) => {
            note = !note;
            Draw();
        };
        hint.Click += async (_, _) => await Aid("hint");
        candidates.Click += async (_, _) => await Aid("candidates");
        pause.Click += async (_, _) => await Move(game?.Paused == true ? "resume" : "pause");
        mainAction.Click += async (_, _) => await Primary();
        void Clock()
        {
            if (game is null)return;
            int seconds = game.Seconds + (connected && !game.Paused && !game.Completed && !game.OutOfLives && game.Reward is null?(int)Math.Min(12, anchor.Elapsed.TotalSeconds): 0);
            elapsed.Text = FormatTime(seconds);
            life.Text = string.Join(" ", Enumerable.Range(0, Game.MaxLives).Select(i => i < game.Lives ? "♥" : "♡"));
            points.Text = Math.Max(0, game.CurrentScore - seconds / 3 + game.Seconds / 3).ToString("N0");
            life.ToolTip = $"Життя: {game.Lives}/{Game.MaxLives}\nПомилки: {game.Mistakes}";
            if (game.OutOfLives && !game.FinalDefeat && game.Reward is null)
            {
                int left = Math.Max(0, (int)Math.Ceiling(reviveDeadline - anchor.Elapsed.TotalMilliseconds / 1000));
                notice.Text = $"Останній шанс: {left} с";
                mainAction.IsEnabled = connected && !working && left > 0;
                if (left == 0 && !working && connected)_ = Load();
            }
        }
        void Accept(OnlineGameView g)
        {
            game = g.Empty?null: g;
            connected = true;
            anchor.Restart();
            reviveDeadline = (game?.ReviveRemainingMs ?? 0) / 1000.0;
            if (game is not null) selected = Math.Clamp(game.Selected, 0, 80);
        }
        void Draw()
        {
            bool active = Active();
            bool visible = game is { Paused: false, Completed: false, OutOfLives: false } && connected;
            grid.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            cover.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            level.Text = game is null ? "Підготовка" : Level(game.Difficulty);
            bool editable = active && game!.Givens[selected] == 0;
            for (int i = 0; i < 81; i++)
            {
                var b = boardCells[i];
                int value = visible ? game!.Values[i] : 0;
                DrawGameCell(b, i, value, visible ? game!.Notes[i] : 0, visible && game!.Givens[i] != 0,
                    visible && i == selected, visible && game!.Wrong[i],
                    visible && value != 0 && value == game!.Values[selected],
                    visible && state.HighlightPeers && Sudoku.Peers[selected].Contains(i));
                // Keep keyboard focus through an in-flight server move. Active() guards input.
                b.IsEnabled = visible && game?.Reward is null;
                b.IsTabStop = visible && i == selected;
            }
            for (int i = 0; i < 9; i++)
            {
                int remaining = game?.Remaining[i] ?? 9;
                DrawGameDigit(padCells[i], i + 1, remaining, editable && (note || remaining > 0));
            }
            int filled = game?.Values.Count(v => v != 0) ?? 0;
            int givens = game?.Givens.Count(v => v != 0) ?? 0;
            SetCompactMetrics(progress, game is null ? [("Стан", "Підготовка поля")] :
                [("Заповнено", $"{filled} / 81"), ("Ваші клітинки", $"{filled - givens} / {81 - givens}"), ("Підказки", game.Hints.ToString())]);
            progressBar.Value = filled;
            erase.IsEnabled = editable;
            notes.IsEnabled = hint.IsEnabled = candidates.IsEnabled = active;
            undo.IsEnabled = active && game!.CanUndo;
            redo.IsEnabled = active && game!.CanRedo;
            hint.Content = $"Підказка ({game?.HintCredits ?? 0})";
            candidates.Content = $"Кандидати ({game?.CandidateCredits ?? 0})";
            candidates.ToolTip = game is null ? null : $"Зміниться {game.CandidateDelta} кандидатів\nШтраф: −{(long)Game.CandidatePointCost * game.CandidateDelta} балів";
            notes.Content = note ? "Нотатки: увімк." : "Нотатки";
            notes.Background = B(note ? "Same" : "Control");
            updatingOptions = true;
            auto.IsChecked = game?.AutoFinish ?? state.AutoFinish;
            updatingOptions = false;
            auto.IsEnabled = connected && !working && game is not { Completed: true } && game is not { OutOfLives: true };
            pause.Content = game?.Paused == true ? "Продовжити" : "Пауза";
            pause.IsEnabled = connected && !working && game is { Completed: false, OutOfLives: false, Reward: null };
            coverTitle.Text = !connected ? "Немає з’єднання" : game is null ? (working ? "Генеруємо поле…" : "Почнімо гру") :
                game.Completed ? "Головоломку розв’язано!" : game.OutOfLives ? "Поразка" : game.Paused ? "Гра на паузі" : "Очікуємо відповідь…";
            mainAction.Content = !connected ? "Відновити з’єднання" : game is null ? "Почати" :
                game.Completed ? "До календаря" : game.FinalDefeat ? "Повторити" : game.OutOfLives ? "Відновити життя за рекламу" : "Продовжити";
            mainAction.IsEnabled = !working && game?.Reward is null;
            if (connected) notice.Text = game is null ? "Головоломка дня: " + day :
                game.Completed ? $"Час: {FormatTime(game.Seconds)}\nБали: {game.CurrentScore:N0}\nПомилки: {game.Mistakes}\nПідказки: {game.Hints}" :
                game.FinalDefeat ? "Час вичерпано." : game.Paused ? "Поле приховано, таймер зупинено." : "";
            Clock();
        }
        async Task Load(bool pauseSession = false)
        {
            if (working)return;
            working = true;
            int oldSelected = selected;
            bool existing = game is not null;
            try
            {
                Accept(await Request<OnlineGameView > ("web/session?challengeDay=" + day + (pauseSession?"&pause=true": "")));
                if (existing)selected = oldSelected;
            }
            catch (Exception ex)
            {
                connected = false;
                notice.Text = ex.Message;
            }
            finally
            {
                working = false;
                if (window.IsVisible)Draw();
            }
        }
        async Task Start()
        {
            if (working || !connected)return;
            working = true;
            Draw();
            try
            {
                Accept(await Request<OnlineGameView > ("web/new", new { difficulty = Difficulty.Medium, id = game?.Id, revision = game?.Revision ?? 0, autoFinish = state.AutoFinish, challengeDay = day }));
            }
            catch (Exception ex)
            {
                notice.Text = ex.Message;
                connected = false;
            }
            finally
            {
                working = false;
                if (window.IsVisible)Draw();
            }
        }
        async Task<bool> Move(string action, int digit = 0, string? reward = null, bool temporaryNote = false)
        {
            if (working || !connected || game is null)return false;
            working = true;
            Draw();
            int cell = selected;
            try
            {
                string wireAction = !state.AutoClean && action is ("enter" or "hint") ? action + "-keep-notes" : action;
                var g = await Request<OnlineGameView > ("web/action", new { id = game.Id, revision = game.Revision,
                    action = wireAction, cell, digit, note = action == "auto-finish" ? !game.AutoFinish : note || temporaryNote, reward, challengeDay = day });
                bool wonNow = game?.Completed != true && g.Completed;
                Accept(g);
                if (wonNow && window.IsVisible)
                {
                    var wonId = g.Id;
                    _ = Dispatcher.BeginInvoke(new Action(() => { if (window.IsVisible) _ = ShowWinReward(wonId, day); }));
                }
                if (action != "hint")selected = cell;
                return true;
            }
            catch (Exception ex)
            {
                connected = false;
                notice.Text = ex.Message;
                return false;
            }
            finally
            {
                working = false;
                if (window.IsVisible)Draw();
            }
        }
        async Task Aid(string kind)
        {
            if (!Active())return;
            int count = kind == "hint"?game!.HintCredits: game!.CandidateCredits;
            if (count > 0)
            {
                await Move(kind);
                return;
            }
            try
            {
                daily = await Request<DailyStatus > ("daily");
            }
            catch
            {
                notice.Text = "Сервіс недоступний.";
                return;
            }
            if (WalletCount(kind) > 0)
            {
                await Move("wallet", reward: kind);
                return;
            }
            await Ad(kind);
        }
        async Task Ad(string kind)
        {
            if (!await Move("reward-start", reward: kind))return;
            if (game?.Reward is null)return;
            var body = Stack();
            body.Children.Add(Text("Демонстраційна реклама", 22, "Text", true));
            var remaining = Text("", 24, "Accent", true);
            body.Children.Add(remaining);
            var ad = Modal("Винагорода", body, 360);
            var clock = Stopwatch.StartNew();
            int length = game.Reward.Remaining;
            var claim = Action("Отримати", () => ad.DialogResult = true, primary: true);
            claim.IsEnabled = false;
            body.Children.Add(claim);
            var cancel = Action("Скасувати", () => ad.DialogResult = false);
            body.Children.Add(cancel);
            var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            tick.Tick += (_, _) => {
                int left = Math.Max(0, (int)Math.Ceiling(length - clock.Elapsed.TotalSeconds));
                remaining.Text = left + " с";
                claim.IsEnabled = left == 0;
            };
            tick.Start();
            bool accepted = ad.ShowDialog() == true;
            tick.Stop();
            await Move(accepted?"reward-claim": "reward-cancel", reward: kind);
        }
        async Task Primary()
        {
            if (!connected)
            {
                await Load(true);
                return;
            }
            if (game is null || game.FinalDefeat)
            {
                await Start();
                return;
            }
            if (game.Completed)
            {
                window.Close();
                return;
            }
            if (game.OutOfLives)
            {
                await Ad("life");
                return;
            }
            if (game.Paused)await Move("resume");
        }
        timer.Tick += (_, _) => Clock();
        heartbeat.Tick += async (_, _) => {
            if (game is { Paused: false, Completed: false, OutOfLives: false, Reward: null } && !working && connected)await Load();
        };
        window.Loaded += async (_, _) => {
            await Load(true);
            if (game?.Reward is not null)await Move("reward-cancel", reward: game.Reward.Kind);
            if (game is null && connected && !resumeOnly)await Start();
            timer.Start();
            heartbeat.Start();
        };
        window.Closing += async (_, e) => {
            if (allowClose)return;
            if (working)
            {
                e.Cancel = true;
                return;
            }
            if (game is { Paused: false, Completed: false, OutOfLives: false } && connected)
            {
                e.Cancel = true;
                window.IsClosingPending = true;
                try
                {
                    if (await Move("pause"))
                    {
                        allowClose = true;
                        window.Close();
                    }
                    else
                    {
                        homeRequested = false;
                        afterHome = null;
                    }
                }
                finally { window.IsClosingPending = false; }
            }
        };
        Func<Task<bool>> pauseForHeader = async () =>
        {
            if (working) return false;
            if (game is { Paused: false, Completed: false, OutOfLives: false, Reward: null })
                return connected && await Move("pause");
            return game?.Reward is null;
        };
        beforePageNavigation = pauseForHeader;
        window.Closed += (_, _) => {
            if (beforePageNavigation == pauseForHeader) beforePageNavigation = null;
            timer.Stop();
            heartbeat.Stop();
        };
        window.MouseHistory = forward => { if (!Active()) return false; _ = Move(forward ? "redo" : "undo"); return true; };
        window.Reopen = () => _ = ShowChallengeGame(day, resumeOnly: true);
        window.RefreshPresentation = Draw;
        window.EscapeAction = () =>
        {
            if (game is not { Completed: false, OutOfLives: false, Reward: null } || !connected) return false;
            if (!working) _ = Move(game.Paused ? "resume" : "pause");
            return true;
        };
        window.PreviewKeyDown += (_, e) =>
        {
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Alt) || mods.HasFlag(ModifierKeys.Windows)) return;
            if (e.Key == Key.P && connected && !working && game is { Completed: false, OutOfLives: false, Reward: null })
                _ = Move(game.Paused ? "resume" : "pause");
            else if (!Active()) return;
            else if (mods.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.Z) _ = Move(mods.HasFlag(ModifierKeys.Shift) ? "redo" : "undo");
                else if (e.Key == Key.Y) _ = Move("redo");
                else return;
            }
            else
            {
                int digit = e.Key >= Key.D0 && e.Key <= Key.D9 ? (int)e.Key - (int)Key.D0 :
                    e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ? (int)e.Key - (int)Key.NumPad0 : -1;
                if (digit >= 0) _ = Move("enter", digit, temporaryNote: mods.HasFlag(ModifierKeys.Shift));
                else if (e.Key is Key.Delete or Key.Back) _ = Move("enter", 0);
                else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
                {
                    int row = selected / 9, col = selected % 9;
                    if (e.Key == Key.Left) col--;
                    if (e.Key == Key.Right) col++;
                    if (e.Key == Key.Up) row--;
                    if (e.Key == Key.Down) row++;
                    Select(Math.Clamp(row, 0, 8) * 9 + Math.Clamp(col, 0, 8));
                    boardCells[selected].Focus();
                }
                else if (e.Key == Key.N || e.Key == Key.Space && Keyboard.FocusedElement is Button button && boardCells.Contains(button))
                {
                    note = !note;
                    Draw();
                }
                else if (e.Key == Key.H) _ = Aid("hint");
                else if (e.Key == Key.A) _ = Aid("candidates");
                else return;
            }
            e.Handled = true;
        };
        Draw();
        window.ShowDialog();
        if (game?.Completed == true || game?.FinalDefeat == true) await Sync();
    }
}
