using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

public sealed class MainWindow : Window
{
    private readonly LocalStore store = new(LocalStore.DefaultPath, StateStore.DefaultPath);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private AccountSession? account;
    private bool syncing, reachable, probing;
    private DateTimeOffset nextProbe = DateTimeOffset.MinValue;
    private readonly string serverUrl = ServiceUrl();
    private DateTimeOffset nextSync = DateTimeOffset.MinValue;
    private string Scope => state.AccountScope ?? "";
    private readonly AppState state;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch clock = new();
    private readonly Button[] cells = new Button[81], digits = new Button[9], levels = new Button[4];
    private Button undo = null!, redo = null!, erase = null!, hint = null!, notes = null!, fill = null!,
        pause = null!, restart = null!, newGame = null!, theme = null!;
    private Grid board = null!, cover = null!;
    private TextBlock time = null!, errors = null!, progress = null!, heading = null!, status = null!,
        coverTitle = null!, coverDetail = null!, stats = null!, saveStatus = null!;
    private ProgressBar progressBar = null!;
    private Button coverButton = null!;
    private Difficulty nextDifficulty;
    private CancellationTokenSource? generation;
    private int lastSavedSecond, accountedSeconds;
    private bool closing, watchingReward, spending;
    private DailyStatus? daily;
    private TextBlock lives = null!, score = null!, quest = null!;
    private Button rewardLife = null!, rewardHint = null!, rewardCandidates = null!;
    private Game? G => state.Game;
    private bool Busy => generation is not null;
    private bool Playing => G is { Paused: false, Completed: false, OutOfLives: false } && !Busy && !watchingReward && state.PendingReward is null;

    public MainWindow()
    {
        state = store.Load();
        state.Game?.UpgradeRules();
        account = RestoreSession();
        if (state.Game is not null && state.GameId == Guid.Empty) { state.GameId = Guid.NewGuid(); state.Started = DateTimeOffset.UtcNow; }
        nextDifficulty = state.NextDifficulty ?? G?.Puzzle.Difficulty ?? Difficulty.Medium;
        Title = "NeSud • Судоку";
        MinWidth = 790; MinHeight = 620;
        Width = Math.Clamp(state.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(state.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        ApplyTheme();
        Build();
        if (state.Maximized) WindowState = WindowState.Maximized;
        PreviewKeyDown += OnKey;
        timer.Tick += (_, _) =>
        {
            ReadClock();
            if (G is not null && G.Seconds - lastSavedSecond >= 5) Save();
            if (account is not null && !syncing && DateTimeOffset.UtcNow >= nextSync) _ = Sync();
            if (!probing && DateTimeOffset.UtcNow >= nextProbe) _ = Probe();
        };
        timer.Start();
        Closing += (_, _) =>
        {
            closing = true; generation?.Cancel(); ReadClock(); clock.Stop(); timer.Stop();
            state.Width = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
            state.Height = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height;
            state.Maximized = WindowState == WindowState.Maximized;
            Save();
        };
        Loaded += async (_, _) =>
        {
            if (G is null) await Generate(false);
            else { SyncClock(); Render(); FocusCell(); if (G.Completed) status.Text = "Відновлено завершену головоломку."; }
            if (store.Warning is { } warning) status.Text = warning;
        };
        Render();
    }

    private static string Level(Difficulty d) => d switch
    {
        Difficulty.Easy => "Легка", Difficulty.Medium => "Середня",
        Difficulty.Hard => "Складна", _ => "Експерт"
    };

    private Brush B(string key) => (Brush)Resources[key + "Brush"];
    private static SolidColorBrush ColorBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b;
    }

    private void ApplyTheme()
    {
        string[] names = ["Background", "Panel", "Text", "Muted", "Control", "Line", "StrongLine",
            "Accent", "AccentText", "Peer", "Same", "Selected", "Given", "Wrong", "WrongBg"];
        string[] colors = state.Dark
            ? ["#101722", "#182230", "#E8EFF8", "#A1B2C8", "#233247", "#38495E", "#7890AC",
               "#75C9FF", "#0D2031", "#23374C", "#30506B", "#416B8E", "#F1F5FC", "#FF9EA8", "#512C3B"]
            : ["#F1F5F9", "#FFFFFF", "#192C42", "#53677F", "#E8EFF6", "#C9D5E2", "#758CA5",
               "#1769A8", "#FFFFFF", "#E2EFF9", "#C7E0F5", "#A8D0F0", "#192C42", "#A92335", "#FBE2E5"];
        for (int i = 0; i < names.Length; i++)
        {
            var brush = ColorBrush(colors[i]);
            Resources[names[i] + "Brush"] = brush;
            Application.Current.Resources[names[i] + "Brush"] = brush;
        }
        Background = B("Background"); Foreground = B("Text");
    }

    private TextBlock Text(string value, double size = 14, string color = "Text", bool bold = false)
    {
        var t = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, color + "Brush");
        return t;
    }

    private Button Action(string label, Action action, string tip = "", bool primary = false)
    {
        var b = new Button { Content = label, Margin = new Thickness(0, 0, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 39 };
        if (tip.Length > 0) b.ToolTip = tip;
        if (primary)
        {
            b.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            b.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush");
            b.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            b.FontWeight = FontWeights.SemiBold;
        }
        b.Click += (_, _) => action();
        return b;
    }

    private Border Card(UIElement content, double padding = 16)
    {
        var b = new Border { Child = content, Padding = new Thickness(padding), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return b;
    }

    private static void At(Grid grid, UIElement element, int row, int col, int span = 1)
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, col); Grid.SetColumnSpan(element, span);
        grid.Children.Add(element);
    }
    private static StackPanel Stack() => new();
    private static void Gap(StackPanel panel, double h = 10) => panel.Children.Add(new Border { Height = h });

    private void Build()
    {
        var root = new Grid { Margin = new Thickness(24, 18, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = Stack();
        brand.Children.Add(Text("NeSud", 30, "Text", true));
        brand.Children.Add(Text("Логіка. Спокій. Концентрація.", 13, "Muted"));
        At(header, brand, 0, 0);
        theme = Action("Тема", () => { state.Dark = !state.Dark; ApplyTheme(); Render(); Save(); }, "Світла / темна тема");
        theme.VerticalAlignment = VerticalAlignment.Center; theme.MinWidth = 130;
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new[] { Action("Історія", ShowHistory), Action("Рейтинг", () => _ = ShowRanking()), Action("Акаунт", ShowAccount), theme })
        { b.Margin = new Thickness(5, 0, 0, 0); headerActions.Children.Add(b); }
        At(header, headerActions, 0, 1); At(root, header, 0, 0, 3);

        var play = new Grid();
        play.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        play.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        play.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var info = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 4; i++) info.ColumnDefinitions.Add(new ColumnDefinition());
        heading = Text("", 19, "Text", true);
        time = Text("00:00", 19, "Text", true);
        errors = Text("0", 19, "Text", true);
        At(info, Metric("СКЛАДНІСТЬ", heading), 0, 0);
        At(info, Metric("ЧАС", time), 0, 1);
        lives = Text("♥ ♥ ♥", 19, "Wrong", true); score = Text("0", 19, "Accent", true);
        At(info, Metric("ЖИТТЯ", lives), 0, 2); At(info, Metric("БАЛИ", score), 0, 3);
        errors.Visibility = Visibility.Collapsed;
        At(play, info, 0, 0);

        var boardArea = new Grid();
        var view = new Viewbox { Stretch = Stretch.Uniform, MaxWidth = 650, MaxHeight = 650 };
        board = BuildBoard();
        view.Child = board; boardArea.Children.Add(view);
        cover = new Grid();
        cover.SetResourceReference(Panel.BackgroundProperty, "PanelBrush");
        var center = Stack(); center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center; center.MaxWidth = 320;
        coverTitle = Text("", 27, "Text", true); coverTitle.TextAlignment = TextAlignment.Center;
        coverDetail = Text("", 15, "Muted"); coverDetail.TextAlignment = TextAlignment.Center;
        coverButton = Action("Продовжити", () =>
        {
            if (Busy) generation?.Cancel();
            else if (G is { Completed: true }) _ = Generate(true);
            else if (G is { OutOfLives: true }) _ = ChooseResource("life");
            else if (G is { Paused: true }) TogglePause();
            else _ = Generate(false);
        }, primary: true);
        center.Children.Add(coverTitle); Gap(center); center.Children.Add(coverDetail);
        Gap(center, 18); center.Children.Add(coverButton); cover.Children.Add(center);
        boardArea.Children.Add(cover); At(play, boardArea, 1, 0);

        var bottom = Stack(); bottom.Margin = new Thickness(0, 14, 0, 0);
        progress = Text("", 13, "Muted");
        progressBar = new ProgressBar { Minimum = 0, Maximum = 81, Height = 5, Margin = new Thickness(0, 7, 0, 0),
            BorderThickness = new Thickness(0) };
        progressBar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        progressBar.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        bottom.Children.Add(progress); bottom.Children.Add(progressBar); At(play, bottom, 2, 0);
        At(root, Card(play), 1, 0);

        var side = Stack();
        side.Children.Add(Text("Нова гра", 19, "Text", true)); Gap(side, 6);
        var levelGrid = new UniformGrid { Columns = 2 };
        for (int i = 0; i < 4; i++)
        {
            var d = (Difficulty)i;
            levels[i] = Action(Level(d), () => { state.NextDifficulty = nextDifficulty = d; Render(); Save(); }, "Обрати складність наступної гри");
            levels[i].Margin = new Thickness(2, 2, 2, 3); levelGrid.Children.Add(levels[i]);
        }
        side.Children.Add(levelGrid);
        newGame = Action("Нова головоломка", () => _ = Generate(true), "Ctrl+N", true);
        side.Children.Add(newGame);
        Gap(side, 12);
        side.Children.Add(Text("Цифри та нотатки", 17, "Text", true)); Gap(side, 5);
        var pad = new UniformGrid { Columns = 3 };
        for (int i = 0; i < 9; i++)
        {
            int d = i + 1;
            digits[i] = Action(d.ToString(), () => Enter(d), $"Клавіша {d}");
            digits[i].Margin = new Thickness(2); digits[i].Padding = new Thickness(3, 4, 3, 4);
            digits[i].MinHeight = 49; pad.Children.Add(digits[i]);
        }
        side.Children.Add(pad); Gap(side, 8);
        var actions = new UniformGrid { Columns = 2 };
        undo = Action("↶  Скасувати", () => Change(g => g.Undo()), "Ctrl+Z");
        redo = Action("↷  Повторити", () => Change(g => g.Redo()), "Ctrl+Y / Ctrl+Shift+Z");
        erase = Action("Стерти", () => Enter(0), "Delete / Backspace / 0");
        notes = Action("Нотатки", ToggleNotes, "N або Space; Shift+цифра для разової нотатки");
        hint = Action("Підказка", Hint, "H");
        fill = Action("Кандидати", Candidates, "A: заповнити всі можливі нотатки");
        pause = Action("Пауза", TogglePause, "P або Esc");
        restart = Action("Заново", Restart, "Почати цю головоломку спочатку");
        foreach (var b in new[] { undo, redo, erase, notes, hint, fill, pause, restart })
        { b.Margin = new Thickness(2); actions.Children.Add(b); }
        side.Children.Add(actions); Gap(side, 10);
        side.Children.Add(Text("Винагороди", 16, "Text", true));
        side.Children.Add(Action("Щоденні нагороди", () => _ = ShowDaily(), primary: true));
        quest = Text("", 12, "Muted"); side.Children.Add(quest); Gap(side, 5);
        rewardLife = Action("+1 життя · 8 с", () => Reward("life"));
        rewardHint = Action("+1 підказка · 8 с", () => Reward("hint"));
        rewardCandidates = Action("+1 кандидати · 8 с", () => Reward("candidates"));
        foreach (var b in new[] { rewardLife, rewardHint, rewardCandidates }) side.Children.Add(b);
        Gap(side, 10);
        side.Children.Add(Text("Налаштування", 16, "Text", true));
        side.Children.Add(Option("Підсвічувати помилки", state.ShowMistakes, v => state.ShowMistakes = v));
        side.Children.Add(Option("Підсвічувати пов’язані клітинки", state.HighlightPeers, v => state.HighlightPeers = v));
        side.Children.Add(Option("Автоматично очищати нотатки", state.AutoClean, v => state.AutoClean = v));
        Gap(side, 9);
        stats = Text("", 12, "Muted"); side.Children.Add(stats);
        var help = Action("Клавіатурне керування", ShowHelp, "F1"); Gap(side, 8); side.Children.Add(help);
        var scroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        At(root, Card(scroll, 13), 1, 2);
        var footer = new Grid { Margin = new Thickness(3, 10, 3, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status = Text("Оберіть клітинку або використовуйте стрілки.", 12, "Muted");
        saveStatus = Text("Локальне збереження", 12, "Muted");
        saveStatus.Margin = new Thickness(12, 0, 0, 0);
        At(footer, status, 0, 0); At(footer, saveStatus, 0, 1); At(root, footer, 2, 0, 3);
        Content = root;
    }

    private StackPanel Metric(string label, TextBlock value)
    {
        var s = Stack(); s.Children.Add(Text(label, 11, "Muted", true)); s.Children.Add(value); return s;
    }
    private CheckBox Option(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            IsChecked = value };
        c.Checked += (_, _) => { set(true); Render(); Save(); };
        c.Unchecked += (_, _) => { set(false); Render(); Save(); };
        return c;
    }

    private Grid BuildBoard()
    {
        var result = new Grid { Width = 540, Height = 540, SnapsToDevicePixels = true };
        result.SetResourceReference(Panel.BackgroundProperty, "StrongLineBrush");
        for (int k = 0; k < 9; k++)
        {
            result.RowDefinitions.Add(new RowDefinition());
            result.ColumnDefinitions.Add(new ColumnDefinition());
        }
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.Name = "CellChrome";
        border.AppendChild(presenter); template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "CellChrome"));
        template.Triggers.Add(hover);
        for (int i = 0; i < 81; i++)
        {
            int index = i, r = i / 9, c = i % 9;
            var b = new Button { Template = template, Padding = new Thickness(0), FontSize = 27,
                Margin = new Thickness(c % 3 == 0 ? 2 : 0.5, r % 3 == 0 ? 2 : 0.5,
                    c % 3 == 2 ? 2 : 0.5, r % 3 == 2 ? 2 : 0.5),
                IsTabStop = false, FocusVisualStyle = null, Cursor = Cursors.Hand };
            b.Click += (_, _) => Select(index);
            b.GotKeyboardFocus += (_, _) => { if (Playing && G!.Selected != index) Select(index, false); };
            cells[i] = b; At(result, b, r, c);
        }
        return result;
    }

    private void Render()
    {
        var g = G;
        bool active = Playing, visible = g is not null && !g.Paused && !g.OutOfLives && !Busy;
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
        int selectedValue = g is null ? 0 : g.Values[g.Selected];
        for (int i = 0; i < 81; i++)
        {
            var b = cells[i]; b.IsTabStop = active && i == g!.Selected;
            if (g is null) { b.Content = null; b.Background = B("Panel"); continue; }
            int value = g.Values[i];
            bool given = g.Puzzle.Givens[i] != 0, selected = i == g.Selected;
            bool wrong = state.ShowMistakes && value != 0 && value != g.Puzzle.Solution[i];
            bool same = selectedValue != 0 && selectedValue == value;
            bool peer = state.HighlightPeers && Sudoku.Peers[g.Selected].Contains(i);
            string background = selected ? "Selected" : wrong ? "WrongBg" : same ? "Same" : peer ? "Peer" : "Panel";
            b.Background = B(background);
            string fg = wrong ? "Wrong" : given ? "Given" : "Accent";
            b.Foreground = B(fg);
            b.FontWeight = given ? FontWeights.SemiBold : FontWeights.Normal;
            b.Content = value != 0 ? value.ToString() : g.Notes[i] != 0 ? NoteGrid(g.Notes[i]) : null;
            AutomationProperties.SetName(b, $"Рядок {i / 9 + 1}, стовпець {i % 9 + 1}, " +
                (value == 0 ? "порожньо" : value.ToString()) + (given ? ", задано" : "") +
                (wrong ? ", помилка" : "") + (selected ? ", вибрано" : ""));
        }
        for (int i = 0; i < 9; i++)
        {
            int d = i + 1;
            int remaining = g is null ? 9 : Math.Max(0, 9 - g.Values.Count(v => v == d));
            var stack = Stack(); stack.Children.Add(Text(d.ToString(), 21, "Text", true));
            var count = Text($"ще {remaining}", 10, "Muted"); count.TextAlignment = TextAlignment.Center;
            ((TextBlock)stack.Children[0]).TextAlignment = TextAlignment.Center;
            stack.Children.Add(count); digits[i].Content = stack;
            digits[i].IsEnabled = active && g!.Editable(g.Selected);
            digits[i].Opacity = remaining == 0 ? 0.55 : 1;
            AutomationProperties.SetName(digits[i], $"Цифра {d}, залишилось {remaining}");
        }
        int filled = g?.Values.Count(v => v != 0) ?? 0;
        int givens = g?.Puzzle.Givens.Count(v => v != 0) ?? 0;
        progress.Text = g is null ? "Підготовка поля" : g.Completed ? $"✓ Судоку розв’язано!  •  Час: {FormatTime(g.Seconds)}  •  Підказки: {g.Hints}" : $"Заповнено {filled}/81  •  Ваші клітинки: {filled - givens}/{81 - givens}  •  Підказки: {g.Hints}";
        progressBar.Value = filled;
        undo.IsEnabled = active && g!.UndoStack.Count > 0; redo.IsEnabled = active && g!.RedoStack.Count > 0;
        erase.IsEnabled = active && g!.Editable(g.Selected); hint.IsEnabled = fill.IsEnabled = notes.IsEnabled = active;
        hint.Content = $"Підказка ({g?.HintCredits ?? 0})"; fill.Content = $"Кандидати ({g?.CandidateCredits ?? 0})";
        fill.ToolTip = g is null ? null : $"Зміниться {g.CandidateDelta()} кандидатів • −{(long)Game.CandidatePointCost * g.CandidateDelta()} балів";
        quest.Text = account is null ? "Щоденні нагороди доступні в акаунті." : daily is null ? "Щоденні нагороди • онлайн" : $"Сьогодні: {daily.Wins}/7 перемог • карток: {daily.Packs.Count}";
        rewardLife.IsEnabled = !Busy && !watchingReward && g?.CanReward("life") == true;
        rewardHint.IsEnabled = !Busy && !watchingReward && g?.CanReward("hint") == true;
        rewardCandidates.IsEnabled = !Busy && !watchingReward && g?.CanReward("candidates") == true;
        notes.Content = g?.NotesMode == true ? "Нотатки: увімк." : "Нотатки";
        notes.Background = B(g?.NotesMode == true ? "Same" : "Control");
        pause.IsEnabled = g is not null && !Busy && !g.Completed && !g.OutOfLives;
        pause.Content = g?.Paused == true ? "Продовжити" : "Пауза";
        restart.IsEnabled = g is not null && !Busy && state.PendingReward is null; newGame.IsEnabled = !Busy && state.PendingReward is null;
        theme.Content = state.Dark ? "☀  Світла тема" : "☾  Темна тема";
        board.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        cover.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        coverTitle.Text = Busy ? "Генеруємо поле…" : g?.Completed == true ? "Судоку розв’язано!" :
            g?.OutOfLives == true ? "Життя закінчилися" : g?.Paused == true ? "Гра на паузі" : "Почнімо гру";
        coverDetail.Text = Busy ? "Перевіряємо єдиність розв’язку." : g?.Completed == true ?
            $"Час: {FormatTime(g.Seconds)} • Помилки: {g.Mistakes} • Підказки: {g.Hints}" :
            g?.OutOfLives == true ? "Відновіть одне життя за 8-секундну рекламу або почніть нову гру." :
            g?.Paused == true ? "Поле приховано, таймер зупинено." : "Оберіть складність і створіть головоломку.";
        coverButton.Content = Busy ? "Скасувати" : g?.OutOfLives == true ? "+1 життя · 8 с" : g?.Paused == true ? "Продовжити" : "Нова гра";
        UpdateConnection();
    }

    private UniformGrid NoteGrid(int mask)
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 51, Height = 51 };
        for (int n = 1; n <= 9; n++)
        {
            var t = Text((mask & (1 << n)) != 0 ? n.ToString() : "", 13, "Muted");
            t.TextAlignment = TextAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(t);
        }
        return grid;
    }

    private static string FormatTime(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
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
        score.ToolTip = G is null ? null : $"Час: −{G.Seconds / 3} • Помилки: −{75L * G.Mistakes} • Підказки: −{150L * G.Hints} • Кандидати: −{G.EffectiveCandidatePenalty} • Відновлення: −{100L * G.LifeRestores}";
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
            store.Save(state, previous ?? (G is { Completed: true } ? Terminal("completed") : null), previousScope ?? state.GameScope ?? ""); lastSavedSecond = G?.Seconds ?? 0;
            if (previous is not null || G is { Completed: true } && !syncing && nextSync > DateTimeOffset.UtcNow.AddSeconds(10))
                nextSync = DateTimeOffset.UtcNow.AddSeconds(10);
            saveStatus.Text = "Збережено ✓"; saveStatus.Foreground = B("Muted"); saveStatus.ToolTip = LocalStore.DefaultPath;
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
        if (!Playing || !action(G!)) return;
        if (G!.OutOfLives) { SyncClock(); status.Text = "Життя закінчилися. Відновіть життя або створіть нову гру."; }
        else if (G.Completed)
        {
            SyncClock();
            if (!G.Awarded)
            {
                if (!state.Stats.TryGetValue(G.Puzzle.Difficulty, out var s))
                    state.Stats[G.Puzzle.Difficulty] = s = new Statistics();
                s.Wins++;
                if (G.Hints == 0 && G.Mistakes == 0 && G.CandidateUses == 0 && G.LifeRestores == 0)
                    s.BestSeconds = Math.Min(s.BestSeconds ?? int.MaxValue, G.Seconds);
                G.Awarded = true;
            }
            status.Text = $"Головоломку завершено! {G.CurrentScore:N0} балів.";
        }
        else status.Text = message ?? "Хід збережено.";
        Render(); Save(); FocusCell();
    }
    private void Enter(int d, bool temporaryNote = false) => Change(g =>
        g.Enter(g.Selected, d, temporaryNote || g.NotesMode, state.AutoClean));
    private void ToggleNotes()
    {
        if (!Playing) return;
        G!.NotesMode = !G.NotesMode; status.Text = G.NotesMode ? "Режим нотаток увімкнено." : "Режим введення цифр.";
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
        if (delta == 0) { status.Text = "Кандидати вже актуальні."; return; }
        Change(g => g.FillNotes(), $"Кандидати: {delta} змін • −{delta * Game.CandidatePointCost} балів.");
    }

    private static string ResourceName(string kind) => kind switch
    { "life" => "Життя", "hint" => "Підказки", _ => "Кандидати" };
    private int WalletCount(string kind) => kind switch
    { "life" => daily?.Lives ?? 0, "hint" => daily?.Hints ?? 0, _ => daily?.Candidates ?? 0 };

    private async Task ChooseResource(string kind)
    {
        if (account is not null && reachable)
        {
            try { daily = await Request<DailyStatus>("daily"); }
            catch { }
            if (WalletCount(kind) > 0 && Dialog("Ресурс зі сховища", $"Використати 1 одиницю: {ResourceName(kind)}?", true))
            { await Spend(kind); return; }
        }
        Reward(kind);
    }

    private async Task Spend(string kind)
    {
        if (account is null || !reachable || state.PendingReward is not null || G?.CanReward(kind) != true) return;
        var pending = new PendingReward(Scope, new WalletRequest(Guid.NewGuid(), state.GameId, kind));
        state.PendingReward = pending;
        try { store.Save(state); }
        catch { state.PendingReward = null; status.Text = "Не вдалося зберегти операцію."; return; }
        SyncClock(); Render(); await ResumeSpend();
    }

    private async Task ResumeSpend()
    {
        if (spending || account is null || state.PendingReward is not { } pending ||
            pending.Scope != Scope || pending.Request.GameId != state.GameId || G is null) return;
        spending = true;
        var game = G; var session = account;
        try
        {
            var result = await Request<WalletResult>("daily/spend", pending.Request, token: session.Token);
            if (closing || !ReferenceEquals(session, account)) return;
            if (result.Operation != pending.Request.Operation || result.GameId != state.GameId || result.Kind != pending.Request.Kind)
                throw new InvalidDataException("Невірна відповідь.");
            var copy = Database.Decode<Game>(Database.Encode(game));
            if (!copy.GrantReward(result.Kind, advertisement: false)) throw new InvalidDataException("Ресурс не можна застосувати.");
            state.Game = copy; state.PendingReward = null;
            try { store.Save(state); }
            catch { state.Game = game; state.PendingReward = pending; throw; }
            daily = await Request<DailyStatus>("daily", token: session.Token);
            status.Text = "Ресурс зі сховища використано.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            state.PendingReward = null;
            try { store.Save(state); } catch { state.PendingReward = pending; }
            status.Text = "Ресурс недоступний.";
        }
        catch { status.Text = "Очікуємо підтвердження ресурсу. Операцію буде повторено."; }
        finally { spending = false; if (!closing) { SyncClock(); Render(); FocusCell(); } }
    }

    private async Task ShowDaily()
    {
        if (account is null) { ShowAccount(); return; }
        var panel = Stack(); panel.Children.Add(Text("Щоденні нагороди", 24, "Text", true));
        panel.Children.Add(Text("Вхід • 1, 3 і 7 перемог • оберіть одну з трьох закритих карток.", 13, "Muted"));
        Gap(panel);
        var body = Stack(); panel.Children.Add(body);
        var window = Modal("Щоденні нагороди", panel, 540);
        var notice = Text("Завантаження…", 14, "Muted"); panel.Children.Add(notice);
        bool working = false;
        void Draw()
        {
            body.Children.Clear();
            if (daily is not { } d) return;
            body.Children.Add(Text($"Сьогодні: {d.Wins}/7 перемог • доступно карток: {d.Packs.Count}", 15, "Accent", true));
            body.Children.Add(Text($"Сховище: ♥ {d.Lives} • підказки {d.Hints} • кандидати {d.Candidates}", 14));
            Gap(body);
            foreach (var kind in new[] { "life", "hint", "candidates" })
            {
                string k = kind;
                var use = Action($"Використати: {ResourceName(k)} ({WalletCount(k)})", () =>
                { window.Close(); _ = Spend(k); });
                use.IsEnabled = WalletCount(k) > 0 && G?.CanReward(k) == true && state.PendingReward is null;
                body.Children.Add(use);
            }
            if (d.Packs.Count == 0) { body.Children.Add(Text("Усі доступні нагороди отримано.", 13, "Muted")); return; }
            var pack = d.Packs[0];
            string milestone = pack.Milestone switch { "login" => "Щоденний вхід", "win1" => "Перша перемога",
                "win3" => "Третя перемога", _ => $"Сьома перемога • {pack.Slot + 1}/3" };
            Gap(body); body.Children.Add(Text(milestone, 17, "Text", true));
            var cards = new UniformGrid { Columns = 3 }; body.Children.Add(cards);
            for (int i = 0; i < 3; i++)
            {
                int card = i;
                var button = Action("?\nКартка " + (i + 1), () => _ = SelectCard(card), primary: true);
                button.Margin = new Thickness(4); button.MinHeight = 95; button.FontSize = 19; cards.Children.Add(button);
            }
            async Task SelectCard(int index)
            {
                if (working) return; working = true; cards.IsEnabled = false;
                try
                {
                    var result = await Request<DailyResult>("daily/choose", new DailyChoice(pack.Day, pack.Milestone, pack.Slot, index));
                    if (!window.IsVisible) return;
                    daily = result.Status;
                    for (int j = 0; j < cards.Children.Count; j++)
                        if (cards.Children[j] is Button cardButton) cardButton.Content = j == index ? $"{ResourceName(result.Item.Kind)}\n×{result.Item.Amount}" : "Закрито";
                    notice.Text = $"Отримано: {ResourceName(result.Item.Kind)} ×{result.Item.Amount}";
                    body.Children.Add(Action("Далі", () => Draw(), primary: true));
                    notice.Foreground = B("Accent");
                }
                catch { notice.Text = "Не вдалося отримати картку. Повторіть спробу."; cards.IsEnabled = true; }
                finally { working = false; }
            }
        }
        window.Loaded += async (_, _) =>
        {
            try { daily = await Request<DailyStatus>("daily"); if (window.IsVisible) { Draw(); notice.Text = ""; } }
            catch { notice.Text = "Щоденні нагороди доступні при підключенні до сервісу."; }
        };
        var close = Action("Закрити", () => window.Close()); close.IsCancel = true; panel.Children.Add(close);
        window.ShowDialog(); Render(); await Task.CompletedTask;
    }

    private void Reward(string kind)
    {
        if (Busy || watchingReward || state.PendingReward is not null || G is not { } game || !game.CanReward(kind)) return;
        watchingReward = true; SyncClock(); Render();
        var panel = Stack(); panel.Children.Add(Text("Демонстраційна реклама", 22, "Text", true));
        Gap(panel); panel.Children.Add(Text("Після завершення таймера можна отримати винагороду. Це заглушка, а не справжня реклама.", 14, "Muted"));
        Gap(panel); var countdown = Text("", 30, "Accent", true); panel.Children.Add(countdown);
        var bar = new ProgressBar { Minimum = 0, Maximum = Game.RewardSeconds, Height = 7, Margin = new Thickness(0, 8, 0, 18) };
        bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush"); panel.Children.Add(bar);
        var window = Modal("Винагорода", panel, 440);
        var elapsed = Stopwatch.StartNew();
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var claim = Action("Отримати винагороду", () =>
        {
            if (elapsed.Elapsed.TotalSeconds >= Game.RewardSeconds && game == G && game.GrantReward(kind))
                window.DialogResult = true;
        }, primary: true);
        claim.IsEnabled = false; panel.Children.Add(claim);
        var cancel = Action("Скасувати", () => window.DialogResult = false); cancel.IsCancel = true; panel.Children.Add(cancel);
        void Update()
        {
            double passed = elapsed.Elapsed.TotalSeconds; bar.Value = Math.Min(Game.RewardSeconds, passed);
            countdown.Text = passed >= Game.RewardSeconds ? "Готово!" : $"{Math.Ceiling(Game.RewardSeconds - passed)} с";
            claim.IsEnabled = passed >= Game.RewardSeconds;
        }
        watch.Tick += (_, _) => Update(); window.Closed += (_, _) => watch.Stop();
        Update(); watch.Start();
        try { if (window.ShowDialog() == true) { status.Text = "Винагороду отримано."; Save(); } }
        finally { watchingReward = false; SyncClock(); Render(); FocusCell(); }
    }

    private void TogglePause()
    {
        if (G is null || G.Completed || G.OutOfLives || Busy || state.PendingReward is not null) return;
        ReadClock(); G.Paused = !G.Paused; SyncClock(); Render(); Save(); FocusCell();
    }
    private void Restart()
    {
        if (G is null || Busy || watchingReward || state.PendingReward is not null) return;
        if (!Confirm("Очистити всі ходи, нотатки та час цієї гри?")) return;
        ReadClock(); var previous = Terminal(G.Completed ? "completed" : "abandoned"); var scope = state.GameScope ?? "";
        clock.Reset(); state.Game = Game.Start(G.Puzzle); BeginAttempt(); lastSavedSecond = 0;
        SyncClock(); Render(); Save(previous, scope); FocusCell();
        status.Text = "Головоломку розпочато заново.";
    }
    private bool Confirm(string message) => Dialog("Підтвердження", message, true);
    private async Task Generate(bool confirm)
    {
        if (Busy || closing || watchingReward || state.PendingReward is not null) return;
        if (confirm && G is { Completed: false } g && (g.UndoStack.Count > 0 || g.Seconds > 0) &&
            !Confirm("Поточну незавершену гру буде замінено. Створити нову?")) return;
        ReadClock(); clock.Reset();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        generation = cts; var difficulty = nextDifficulty; Render();
        try
        {
            var puzzle = await Task.Run(() => Sudoku.Generate(difficulty, cts.Token), cts.Token);
            if (closing) return;
            var previous = G is null ? null : Terminal(G.Completed ? "completed" : "abandoned");
            string scope = state.GameScope ?? "";
            state.Game = Game.Start(puzzle); BeginAttempt(); lastSavedSecond = 0; Save(previous, scope);
            status.Text = "Нова головоломка. Єдиний розв’язок перевірено.";
        }
        catch (OperationCanceledException)
        { if (!closing) status.Text = "Генерацію скасовано або перевищено час очікування."; }
        catch (Exception ex) { if (!closing) status.Text = ex.Message; }
        finally
        {
            generation = null;
            if (!closing) { SyncClock(); Render(); Save(); FocusCell(); }
        }
    }


    private void BeginAttempt()
    {
        state.GameId = Guid.NewGuid(); state.Started = DateTimeOffset.UtcNow; state.GameScope = Scope;
    }

    private GameRecord Terminal(string outcome) => new(state.GameId, state.Started, DateTimeOffset.UtcNow,
        "desktop", outcome, new Game { Puzzle = G!.Puzzle, Values = (int[])G.Values.Clone(), Notes = (int[])G.Notes.Clone(),
            Seconds = G.Seconds, Mistakes = G.Mistakes, Hints = G.Hints, Selected = G.Selected,
            NotesMode = G.NotesMode, Paused = G.Paused, Awarded = G.Awarded, RulesVersion = G.RulesVersion,
            Lives = G.Lives, HintCredits = G.HintCredits, CandidateCredits = G.CandidateCredits, CandidateUses = G.CandidateUses, CandidatePenalty = G.CandidatePenalty, CandidateChanges = G.CandidateChanges,
            LifeRestores = G.LifeRestores, AdsWatched = G.AdsWatched, ManualCorrect = new HashSet<int>(G.ManualCorrect) });


    private Window Modal(string title, UIElement content, double width = 480, double maxHeight = 660)
    {
        return new Window
        {
            Owner = this, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.Height,
            Width = width, MaxHeight = maxHeight, ShowInTaskbar = false, Background = B("Panel"),
            Foreground = B("Text"), Content = Card(content, 20)
        };
    }

    private bool Dialog(string title, string message, bool confirm)
    {
        var panel = Stack(); panel.Children.Add(Text(title, 23, "Text", true)); Gap(panel, 12);
        panel.Children.Add(Text(message, 15)); Gap(panel, 20);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var window = Modal(title, panel);
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (owner is not null && owner != window) window.Owner = owner;
        if (confirm)
        {
            var cancel = Action("Скасувати", () => window.DialogResult = false);
            cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(cancel);
        }
        var ok = Action(confirm ? "Підтвердити" : "Зрозуміло", () => window.DialogResult = true, primary: true);
        ok.IsDefault = !confirm; actions.Children.Add(ok); panel.Children.Add(actions);
        return window.ShowDialog() == true;
    }

    private async Task<T> Request<T>(string path, object? body = null, string? endpoint = null, string? token = null)
    {
        string url = endpoint ?? serverUrl;
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
            url.TrimEnd('/') + "/api/" + path);
        var auth = token ?? account?.Token;
        if (!string.IsNullOrEmpty(auth)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            try { using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.TryGetProperty("error", out var value)) error = value.GetString(); } catch (System.Text.Json.JsonException) { }
            throw new HttpRequestException(error ?? (response.StatusCode == HttpStatusCode.TooManyRequests ? "Забагато спроб. Спробуйте пізніше." : "Сервіс тимчасово недоступний."), null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("Порожня відповідь сервера.");
    }

    private async Task Sync()
    {
        if (syncing || account is null || closing) return;
        syncing = true; nextSync = DateTimeOffset.UtcNow.AddSeconds(30);
        var session = account; var scope = Scope; var endpoint = serverUrl;
        try
        {
            var pending = store.History(scope, true);
            DesktopDraft? draft = G is { Completed: false } && (state.GameScope ?? "") == scope
                ? new(state.GameId, state.Started, DateTimeOffset.UtcNow, Database.Decode<Game>(Database.Encode(G))) : null;
            foreach (var batch in pending.Chunk(10))
            {
                var records = await Request<List<GameRecord>>("sync", batch, endpoint, session.Token);
                if (closing) return;
                store.Merge(scope, records);
            }
            if (draft is not null) await Request<SyncAck>("desktop/draft", draft, endpoint, session.Token);
            var remote = await Request<List<GameRecord>>("history", endpoint: endpoint, token: session.Token);
            if (closing) return;
            store.Merge(scope, remote);
            if (ReferenceEquals(account, session)) { reachable = true; await ResumeSpend(); var updated = await Request<DailyStatus>("daily", token: session.Token); if (ReferenceEquals(account, session) && !closing) { daily = updated; Render(); } }
        }
        catch (Exception ex)
        {
            if (!closing && ReferenceEquals(account, session))
            {
                if (ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
                { account = null; state.ProtectedSession = null; Save(); }
                else { reachable = false; nextSync = DateTimeOffset.UtcNow.AddSeconds(5); }
                UpdateConnection();
            }
        }
        finally { syncing = false; }
    }

    private static string ServiceUrl()
    {
        var configured = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NeSudServerUrl")?.Value ?? "http://localhost:5080";
        var url = Environment.GetEnvironmentVariable("NESUD_SERVER_URL") ?? configured;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return url.TrimEnd('/');
        return "http://localhost:5080";
    }

    private AccountSession? RestoreSession()
    {
        try
        {
            if (state.ProtectedSession is null || !Scope.StartsWith(serverUrl + "|", StringComparison.Ordinal)) return null;
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(state.ProtectedSession), null, DataProtectionScope.CurrentUser);
            var restored = System.Text.Json.JsonSerializer.Deserialize<AccountSession>(bytes);
            return restored is not null && Scope == serverUrl + "|" + restored.User ? restored : null;
        }
        catch (Exception) { return null; }
    }

    private void RememberSession(AccountSession session)
    {
        try
        {
            state.ProtectedSession = Convert.ToBase64String(ProtectedData.Protect(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(session), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { state.ProtectedSession = null; }
    }

    private void UpdateConnection()
    {
        if (stats is null) return;
        stats.Text = account is not null ? (reachable ? "● Онлайн • " : "○ Офлайн • ") + account.User :
            Scope.Length > 0 ? "Потрібен повторний вхід • локальні дані збережено" :
            reachable ? "● Онлайн • гість" : "○ Офлайн • гість";
    }

    private async Task Probe()
    {
        if (closing || probing) return;
        probing = true;
        bool wasReachable = reachable;
        try { await Request<SyncAck>("health", token: ""); reachable = true; }
        catch { reachable = false; }
        finally
        {
            probing = false; nextProbe = DateTimeOffset.UtcNow.AddSeconds(reachable ? 15 : 5);
            if (!closing)
            {
                UpdateConnection();
                if (reachable && !wasReachable && account is not null) { nextSync = DateTimeOffset.MinValue; _ = Sync(); }
            }
        }
    }

    private (Grid View, Func<string> Value, Action Clear) PasswordInput()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var masked = new PasswordBox { Padding = new Thickness(8), MaxLength = 128,
            Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line") };
        var plain = new TextBox { Padding = new Thickness(8), MaxLength = 128, Visibility = Visibility.Collapsed,
            Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line") };
        bool shown = false;
        Button toggle = null!;
        toggle = Action("Показати", () =>
        {
            shown = !shown;
            if (shown) { plain.Text = masked.Password; masked.Clear(); }
            else { masked.Password = plain.Text; plain.Clear(); }
            masked.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            plain.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = shown ? "Приховати" : "Показати";
            if (shown) plain.Focus(); else masked.Focus();
        });
        toggle.Margin = new Thickness(6, 0, 0, 0); toggle.MinWidth = 100;
        At(grid, masked, 0, 0); At(grid, plain, 0, 0); At(grid, toggle, 0, 1);
        return (grid, () => shown ? plain.Text : masked.Password, () => { masked.Clear(); plain.Clear(); });
    }

    private void ShowAccount()
    {
        if (state.PendingReward is not null && account is not null) { Dialog("Винагорода", "Спочатку дочекайтеся підтвердження отримання ресурсу.", false); return; }
        var panel = Stack(); panel.Children.Add(Text("Акаунт", 24, "Text", true)); Gap(panel);
        var window = Modal("Акаунт", panel, 480);
        if (account is not null)
        {
            panel.Children.Add(Text(account.User, 18, "Text", true));
            panel.Children.Add(Text(reachable ? "● Онлайн • автоматична синхронізація" : "○ Офлайн • синхронізація після відновлення зв’язку", 13, "Muted"));
            Gap(panel, 18);
            panel.Children.Add(Action("Синхронізувати зараз", () => { window.Close(); _ = Sync(); }, primary: true));
            panel.Children.Add(Action("Вийти з акаунта", () =>
            {
                if (!Dialog("Вихід", "Перейти в гостьовий режим? Локальні ігри акаунта залишаться у його історії.", true)) return;
                var old = account; account = null; state.ProtectedSession = null; state.AccountScope = ""; daily = null;
                SwitchProfileGame(); Save(); window.Close(); if (old is not null) _ = Revoke(old, serverUrl);
            }));
        }
        else
        {
            var title = Text("Вхід до акаунта", 16, "Muted", true); panel.Children.Add(title); Gap(panel);
            var tabs = new UniformGrid { Columns = 2 }; panel.Children.Add(tabs);
            bool register = false, working = false;
            var name = new TextBox { Padding = new Thickness(8), MaxLength = 254, Margin = new Thickness(0, 4, 0, 12),
                Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line"),
                Text = Scope.Length == 0 ? "" : Scope.Split('|')[^1] };
            var password = PasswordInput(); var confirm = PasswordInput();
            var confirmPanel = Stack(); confirmPanel.Children.Add(Text("Підтвердження пароля")); confirmPanel.Children.Add(confirm.View);
            var notice = Text("", 13, "Wrong");
            Button submit = null!, loginTab = null!, registerTab = null!;
            void Mode(bool value)
            {
                register = value; confirmPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                confirm.Clear(); notice.Text = ""; submit.Content = value ? "Зареєструватися" : "Увійти";
                title.Text = value ? "Створення акаунта" : "Вхід до акаунта";
                loginTab.Background = B(value ? "Control" : "Same"); registerTab.Background = B(value ? "Same" : "Control");
            }
            loginTab = Action("Вхід", () => Mode(false)); registerTab = Action("Реєстрація", () => Mode(true));
            tabs.Children.Add(loginTab); tabs.Children.Add(registerTab); Gap(panel);
            panel.Children.Add(Text("Логін або email")); panel.Children.Add(name);
            panel.Children.Add(Text("Пароль (10–128 символів)")); panel.Children.Add(password.View); panel.Children.Add(confirmPanel);
            panel.Children.Add(notice);
            async Task Login()
            {
                if (working) return;
                string secret = password.Value(), confirmation = confirm.Value();
                if (state.PendingReward is not null && !string.Equals(Scope.Split('|')[^1], name.Text.Trim(), StringComparison.OrdinalIgnoreCase)) { notice.Text = "Увійдіть до попереднього акаунта для відновлення винагороди."; return; }
                if (name.Text.Trim().Length < 3) { notice.Text = "Введіть логін або email."; name.Focus(); return; }
                if (secret.Length is < 10 or > 128) { notice.Text = "Пароль має містити від 10 до 128 символів."; return; }
                if (register && secret != confirmation) { notice.Text = "Паролі не збігаються."; return; }
                working = true; submit.IsEnabled = loginTab.IsEnabled = registerTab.IsEnabled = false; notice.Text = "Підключення…";
                try
                {
                    var session = await Request<AccountSession>(register ? "register" : "login",
                        new Credentials(name.Text, secret, register ? confirmation : null), token: "");
                    if (!window.IsVisible) return;
                    account = session; reachable = true; state.AccountScope = serverUrl + "|" + session.User;
                    RememberSession(session);
                    bool claim = (store.History("").Count > 0 || (string.IsNullOrEmpty(state.GameScope) && G is { Completed: false }))
                        && Dialog("Імпорт ігор гостя", "Прив’язати локальні ігри гостя до цього акаунта?", true);
                    if (claim) { store.ClaimGuest(Scope); if (string.IsNullOrEmpty(state.GameScope) && G is not null) state.GameScope = Scope; }
                    SwitchProfileGame(); password.Clear(); confirm.Clear(); Save(); UpdateConnection(); window.Close(); _ = Sync();
                }
                catch (HttpRequestException ex) when (ex.StatusCode is not null) { notice.Text = ex.Message; }
                catch { notice.Text = "Не вдалося з’єднатися з сервісом. Спробуйте ще раз."; }
                finally { working = false; submit.IsEnabled = loginTab.IsEnabled = registerTab.IsEnabled = true; }
            }
            submit = Action("Увійти", () => _ = Login(), primary: true); submit.IsDefault = true;
            panel.Children.Add(submit); Mode(false);
            if (Scope.Length > 0 && state.PendingReward is null) panel.Children.Add(Action("Перейти в гостьовий режим", () =>
            {
                state.ProtectedSession = null; state.AccountScope = ""; daily = null; SwitchProfileGame(); Save(); window.Close();
            }));
        }
        var close = Action("Закрити", () => window.Close()); close.IsCancel = true; panel.Children.Add(close);
        window.ShowDialog();
    }

    private void SwitchProfileGame()
    {
        if (state.PendingReward is not null) return;
        if (G is null || (state.GameScope ?? "") == Scope) return;
        ReadClock();
        var previous = Terminal(G.Completed ? "completed" : "abandoned");
        var oldScope = state.GameScope ?? "";
        clock.Reset(); state.Game = null; state.GameId = Guid.Empty; state.GameScope = Scope;
        Save(previous, oldScope); Render();
    }

    private async Task Revoke(AccountSession session, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/api/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var response = await http.SendAsync(request);
        }
        catch { }
    }

    private void ShowHistory()
    {
        var records = store.History(Scope);
        var panel = Stack(); panel.Children.Add(Text("Історія та статистика", 24, "Text", true)); Gap(panel);
        var wins = records.Where(r => r.Outcome == "completed").ToList();
        panel.Children.Add(Text($"Ігор: {records.Count}   Перемог: {wins.Count}   Балів: {wins.Sum(r => (long)r.Score)}", 16));
        panel.Children.Add(Text($"Час: {FormatTime(records.Sum(r => r.Game.Seconds))} • Помилки: {records.Sum(r => (long)r.Game.Mistakes)} • Підказки: {records.Sum(r => (long)r.Game.Hints)}", 13, "Muted"));
        foreach (var d in Enum.GetValues<Difficulty>())
        {
            var games = records.Where(r => r.Game.Puzzle.Difficulty == d).ToList();
            var done = games.Where(r => r.Outcome == "completed").ToList();
            string best = done.Count == 0 ? "немає" : FormatTime(done.Min(r => r.Game.Seconds));
            panel.Children.Add(Text($"{Level(d)}: {done.Count}/{games.Count} перемог • найкращий час: {best}", 13, "Muted"));
        }
        Gap(panel, 12);
        if (G is { Completed: false } && (state.GameScope ?? "") == Scope)
            panel.Children.Add(Text($"Поточна гра: {Level(G.Puzzle.Difficulty)} • {FormatTime(G.Seconds)} • у процесі", 14, "Accent", true));
        var list = Stack();
        foreach (var r in records)
        {
            var item = Stack();
            item.Children.Add(Text($"{Level(r.Game.Puzzle.Difficulty)} • {(r.Outcome == "completed" ? "Перемога" : "Перервано")} • {r.Score} балів", 15, "Text", true));
            item.Children.Add(Text($"{r.Ended.ToLocalTime():g} • {r.Source} • {FormatTime(r.Game.Seconds)} • помилки: {r.Game.Mistakes} • підказки: {r.Game.Hints} • кандидати: {r.Game.CandidateUses} • відновлення життів: {r.Game.LifeRestores}", 12, "Muted"));
            var card = Card(item, 10); card.Margin = new Thickness(0, 0, 0, 6); list.Children.Add(card);
        }
        if (records.Count == 0) list.Children.Add(Text("Завершених або перерваних ігор ще немає.", 14, "Muted"));
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Gap(panel); var window = Modal("Історія", panel, 680, 720);
        var close = Action("Закрити", () => window.Close(), primary: true); close.IsCancel = true; panel.Children.Add(close);
        window.ShowDialog();
    }

    private async Task ShowRanking()
    {
        var panel = Stack(); panel.Children.Add(Text("Рейтинг", 24, "Text", true));
        panel.Children.Add(Text("Серверні результати та загальні бали з офлайн-іграми.", 13, "Muted"));
        var indicator = Text("Завантаження…", 13, "Muted"); panel.Children.Add(indicator); Gap(panel);
        var rows = Stack();
        panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Gap(panel); var window = Modal("Рейтинг", panel, 560);
        var close = Action("Закрити", () => window.Close(), primary: true); close.IsCancel = true; panel.Children.Add(close);
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        bool loading = false;
        async Task Reload()
        {
            if (loading || !window.IsVisible) return;
            loading = true;
            try
            {
                var ranks = await Request<List<RankEntry>>("ranking", token: "");
                var allRanks = await Request<List<RankEntry>>("ranking?mode=all", token: "");
                if (!window.IsVisible) return;
                rows.Children.Clear();
                void Table(string title, List<RankEntry> data)
                {
                    rows.Children.Add(Text(title, 18, "Text", true)); Gap(rows, 8);
                    for (int i = 0; i < data.Count; i++)
                    {
                        var r = data[i]; rows.Children.Add(Text($"{i + 1}. {r.User} • {r.Games} перемог • {r.Score} балів", 15)); Gap(rows, 5);
                    }
                    if (data.Count == 0) rows.Children.Add(Text("Ще немає результатів.", 13, "Muted"));
                    Gap(rows, 16);
                }
                Table("Серверний рейтинг", ranks); Table("Загальні бали (офлайн включено)", allRanks);
                indicator.Text = "● Онлайн • оновлено " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch { if (window.IsVisible) indicator.Text = "○ Офлайн • очікуємо відновлення зв’язку. Показані дані можуть бути застарілими."; }
            finally { loading = false; }
        }
        refresh.Tick += async (_, _) => await Reload();
        window.Loaded += async (_, _) => { refresh.Start(); await Reload(); };
        window.Closed += (_, _) => refresh.Stop();
        window.ShowDialog();
        await Task.CompletedTask;
    }

    private void ShowHelp()
    {
        Dialog("Клавіатурне керування", "Стрілки: вибір клітинки\n" +
            "1–9 / NumPad: цифра; Shift+цифра: нотатка\n" +
            "N / Space: режим нотаток\nDelete / Backspace / 0: стерти\n" +
            "Ctrl+Z / Ctrl+Y: скасувати / повторити\nH: підказка; A: усі кандидати\n" +
            "P / Esc: пауза; Ctrl+N: нова гра; F1: довідка\n" +
            "Tab: переміщення між елементами; Enter: активувати кнопку.", false);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        Key k = e.Key; var mods = Keyboard.Modifiers;
        if (k == Key.F1) { ShowHelp(); e.Handled = true; return; }
        if (Busy && k == Key.Escape) { generation?.Cancel(); e.Handled = true; return; }
        if (mods.HasFlag(ModifierKeys.Control))
        {
            if (k == Key.N) _ = Generate(true);
            else if (k == Key.Z && mods.HasFlag(ModifierKeys.Shift)) Change(g => g.Redo());
            else if (k == Key.Z) Change(g => g.Undo());
            else if (k == Key.Y) Change(g => g.Redo());
            else return;
            e.Handled = true; return;
        }
        if (mods.HasFlag(ModifierKeys.Alt) || mods.HasFlag(ModifierKeys.Windows)) return;
        if (k is Key.P or Key.Escape) { TogglePause(); e.Handled = true; return; }
        if (!Playing) return;
        int code = (int)k;
        int d = code >= (int)Key.D0 && code <= (int)Key.D9 ? code - (int)Key.D0 :
            code >= (int)Key.NumPad0 && code <= (int)Key.NumPad9 ? code - (int)Key.NumPad0 : -1;
        if (d >= 0) Enter(d, mods.HasFlag(ModifierKeys.Shift));
        else if (k is Key.Delete or Key.Back) Enter(0);
        else if (k == Key.N || k == Key.Space && Keyboard.FocusedElement is Button b && cells.Contains(b)) ToggleNotes();
        else if (k == Key.H) Hint();
        else if (k == Key.A) Candidates();
        else if (k is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int i = G!.Selected, r = i / 9, c = i % 9;
            if (k == Key.Left) c = Math.Max(0, c - 1);
            if (k == Key.Right) c = Math.Min(8, c + 1);
            if (k == Key.Up) r = Math.Max(0, r - 1);
            if (k == Key.Down) r = Math.Min(8, r + 1);
            Select(r * 9 + c);
        }
        else return;
        e.Handled = true;
    }
}
