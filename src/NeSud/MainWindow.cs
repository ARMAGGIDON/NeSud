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

namespace NeSud;

public sealed class MainWindow : Window
{
    private readonly StateStore store = new(StateStore.DefaultPath);
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
    private bool closing;
    private Game? G => state.Game;
    private bool Busy => generation is not null;
    private bool Playing => G is { Paused: false, Completed: false } && !Busy;

    public MainWindow()
    {
        state = store.Load();
        nextDifficulty = G?.Puzzle.Difficulty ?? Difficulty.Medium;
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
        At(header, theme, 0, 1); At(root, header, 0, 0, 3);

        var play = new Grid();
        play.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        play.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        play.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var info = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 3; i++) info.ColumnDefinitions.Add(new ColumnDefinition());
        heading = Text("", 19, "Text", true);
        time = Text("00:00", 19, "Text", true);
        errors = Text("0", 19, "Text", true);
        At(info, Metric("СКЛАДНІСТЬ", heading), 0, 0);
        At(info, Metric("ЧАС", time), 0, 1);
        At(info, Metric("ПОМИЛКИ", errors), 0, 2);
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
            levels[i] = Action(Level(d), () => { nextDifficulty = d; Render(); }, "Обрати складність наступної гри");
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
        fill = Action("Кандидати", () => Change(g => g.FillNotes()), "A: заповнити всі можливі нотатки");
        pause = Action("Пауза", TogglePause, "P або Esc");
        restart = Action("Заново", Restart, "Почати цю головоломку спочатку");
        foreach (var b in new[] { undo, redo, erase, notes, hint, fill, pause, restart })
        { b.Margin = new Thickness(2); actions.Children.Add(b); }
        side.Children.Add(actions); Gap(side, 12);
        side.Children.Add(Text("Налаштування", 16, "Text", true));
        side.Children.Add(Option("Показувати помилки", state.ShowMistakes, v => state.ShowMistakes = v));
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
        border.AppendChild(presenter); template.VisualTree = border;
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
        bool active = Playing, visible = g is not null && !g.Paused && !Busy;
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
        notes.Content = g?.NotesMode == true ? "Нотатки: увімк." : "Нотатки";
        notes.Background = B(g?.NotesMode == true ? "Same" : "Control");
        pause.IsEnabled = g is not null && !Busy && !g.Completed;
        pause.Content = g?.Paused == true ? "Продовжити" : "Пауза";
        restart.IsEnabled = g is not null && !Busy; newGame.IsEnabled = !Busy;
        theme.Content = state.Dark ? "☀  Світла тема" : "☾  Темна тема";
        board.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        cover.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        coverTitle.Text = Busy ? "Генеруємо поле…" : g?.Completed == true ? "Судоку розв’язано!" :
            g?.Paused == true ? "Гра на паузі" : "Почнімо гру";
        coverDetail.Text = Busy ? "Перевіряємо єдиність розв’язку." : g?.Completed == true ?
            $"Час: {FormatTime(g.Seconds)} • Помилки: {g.Mistakes} • Підказки: {g.Hints}" :
            g?.Paused == true ? "Поле приховано, таймер зупинено." : "Оберіть складність і створіть головоломку.";
        coverButton.Content = Busy ? "Скасувати" : g?.Paused == true ? "Продовжити" : "Нова гра";
        var stat = state.Stats.GetValueOrDefault(g?.Puzzle.Difficulty ?? nextDifficulty);
        stats.Text = $"Перемоги ({Level(g?.Puzzle.Difficulty ?? nextDifficulty)}): {stat?.Wins ?? 0}  •  Рекорд: " +
            (stat?.BestSeconds is int best ? FormatTime(best) : "немає");
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
    }
    private void SyncClock()
    {
        ReadClock();
        if (Playing && !clock.IsRunning) { accountedSeconds = 0; clock.Restart(); }
        else if (!Playing) { clock.Reset(); accountedSeconds = 0; }
    }
    private void Save()
    {
        try
        {
            store.Save(state); lastSavedSecond = G?.Seconds ?? 0;
            saveStatus.Text = "Збережено ✓"; saveStatus.Foreground = B("Muted"); saveStatus.ToolTip = StateStore.DefaultPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        if (G!.Completed)
        {
            SyncClock();
            if (!G.Awarded)
            {
                if (!state.Stats.TryGetValue(G.Puzzle.Difficulty, out var s))
                    state.Stats[G.Puzzle.Difficulty] = s = new Statistics();
                s.Wins++;
                if (G.Hints == 0 && G.Mistakes == 0)
                    s.BestSeconds = Math.Min(s.BestSeconds ?? int.MaxValue, G.Seconds);
                G.Awarded = true;
            }
            status.Text = "Головоломку завершено. Чисті перемоги враховуються в рекорді часу.";
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
        if (!Playing || G!.GetHint() is not { } m) return;
        G.Selected = m.Cell;
        Change(g => g.Enter(m.Cell, m.Digit, false, state.AutoClean, true),
            $"{m.Reason}: рядок {m.Cell / 9 + 1}, стовпець {m.Cell % 9 + 1} = {m.Digit}.");
    }
    private void TogglePause()
    {
        if (G is null || G.Completed || Busy) return;
        ReadClock(); G.Paused = !G.Paused; SyncClock(); Render(); Save(); FocusCell();
    }
    private void Restart()
    {
        if (G is null || Busy) return;
        if (!Confirm("Очистити всі ходи, нотатки та час цієї гри?")) return;
        clock.Reset(); G.Restart(); lastSavedSecond = 0; SyncClock(); Render(); Save(); FocusCell();
        status.Text = "Головоломку розпочато заново.";
    }
    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "NeSud", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    private async Task Generate(bool confirm)
    {
        if (Busy || closing) return;
        if (confirm && G is { Completed: false } g && (g.UndoStack.Count > 0 || g.Seconds > 0) &&
            !Confirm("Поточну незавершену гру буде замінено. Створити нову?")) return;
        ReadClock(); clock.Reset();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        generation = cts; var difficulty = nextDifficulty; Render();
        try
        {
            var puzzle = await Task.Run(() => Sudoku.Generate(difficulty, cts.Token), cts.Token);
            if (closing) return;
            state.Game = Game.Start(puzzle); lastSavedSecond = 0;
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

    private void ShowHelp()
    {
        MessageBox.Show(this, "Стрілки: вибір клітинки\n" +
            "1–9 / NumPad: цифра; Shift+цифра: нотатка\n" +
            "N / Space: режим нотаток\nDelete / Backspace / 0: стерти\n" +
            "Ctrl+Z / Ctrl+Y: скасувати / повторити\nH: підказка; A: усі кандидати\n" +
            "P / Esc: пауза; Ctrl+N: нова гра; F1: довідка\n" +
            "Tab: переміщення між елементами; Enter: активувати кнопку.",
            "Клавіатурне керування", MessageBoxButton.OK, MessageBoxImage.Information);
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
        int d = k is >= Key.D0 and <= Key.D9 ? (int)k - (int)Key.D0 :
            k is >= Key.NumPad0 and <= Key.NumPad9 ? (int)k - (int)Key.NumPad0 : -1;
        if (d >= 0) Enter(d, mods.HasFlag(ModifierKeys.Shift));
        else if (k is Key.Delete or Key.Back) Enter(0);
        else if (k == Key.N || k == Key.Space && Keyboard.FocusedElement is Button b && cells.Contains(b)) ToggleNotes();
        else if (k == Key.H) Hint();
        else if (k == Key.A) Change(g => g.FillNotes());
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
