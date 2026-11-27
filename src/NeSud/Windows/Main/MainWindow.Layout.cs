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
    private static string Level(Difficulty d) => d switch
    {
        Difficulty.Easy => "Легка", Difficulty.Medium => "Середня",
        Difficulty.Hard => "Складна", _ => "Експерт"
    };

    private Brush B(string key) => (Brush)Resources[key + "Brush"];
    private static SolidColorBrush ColorBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
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
        Background = B("Background");
        Foreground = B("Text");
    }

    private TextBlock Text(string value, double size = 14, string color = "Text", bool bold = false)
    {
        var t = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, color + "Brush");
        return t;
    }

    private Button Action(string label, Action? action = null, string tip = "", bool primary = false)
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
        if (action is not null) b.Click += (_, _) => action();
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
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        Grid.SetColumnSpan(element, span);
        grid.Children.Add(element);
    }
    private static StackPanel Stack() => new();
    private static void Gap(StackPanel panel, double h = 10) => panel.Children.Add(new Border { Height = h });

    private void Build()
    {
        var root = new Grid { Margin = new Thickness(24, 0, 24, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

        var header = new Grid { Margin = new Thickness(24, 18, 24, 14) };
        headerBar = header;
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headerBack = Action("‹", () => { if (surfaces.Count > 0) surfaces[^1].Close(); }, "Повернутися до попередньої сторінки");
        headerBack.Visibility = Visibility.Collapsed;
        headerBack.Margin = new Thickness(0, 0, 10, 0);
        brand.Children.Add(headerBack);
        var home = Action("NeSud", NavigateHome, "Головне меню");
        home.FontSize = 30;
        home.FontWeight = FontWeights.SemiBold;
        home.Background = Brushes.Transparent;
        home.BorderBrush = Brushes.Transparent;
        home.Padding = new Thickness(0, 0, 8, 0);
        home.Margin = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(home, "NeSud: головне меню");
        brand.Children.Add(home);
        At(header, brand, 0, 0);
        theme = Action("Тема", () => {
            state.Dark = !state.Dark;
            ApplyTheme();
            Render();
            Save();
        }, "Світла / темна тема");
        theme.VerticalAlignment = VerticalAlignment.Center;
        theme.MinWidth = 130;
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        accountNavigation = Action("Увійти", async () => await NavigateHeader(() => { if (account is null) ShowAccount(); else _ = ShowProfile(null); }));
        foreach (var b in new[] { Action("Довідка", async () => await NavigateHeader(ShowHelp)), accountNavigation, theme })
        { b.Margin = new Thickness(5, 0, 0, 0);
        headerActions.Children.Add(b); }
        At(header, headerActions, 0, 1);


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
        lives = Text("♥ ♥ ♥", 19, "Wrong", true);
        score = Text("0", 19, "Accent", true);
        At(info, Metric("ЖИТТЯ", lives), 0, 2);
        At(info, Metric("БАЛИ", score), 0, 3);
        errors.Visibility = Visibility.Collapsed;
        At(play, info, 0, 0);

        var boardArea = new Grid();
        var view = new Viewbox { Stretch = Stretch.Uniform, MaxWidth = 650, MaxHeight = 650 };
        board = BuildBoard();
        view.Child = board;
        boardArea.Children.Add(view);
        cover = new Grid();
        cover.SetResourceReference(Panel.BackgroundProperty, "PanelBrush");
        var center = Stack();
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.MaxWidth = 320;
        coverTitle = Text("", 27, "Text", true);
        coverTitle.TextAlignment = TextAlignment.Center;
        coverDetail = Text("", 15, "Muted");
        coverDetail.TextAlignment = TextAlignment.Center;
        coverButton = Action("Продовжити", () =>
        {
            if (Busy) generation?.Cancel();
            else if (G is { Completed: true }) _ = Generate(true);
            else if (G is { OutOfLives: true })
            {
                if (G.CanStartRevive(DateTimeOffset.UtcNow)) Reward("life");
                else _ = Generate(true);
            }
            else if (G is { Paused: true }) TogglePause();
            else _ = Generate(false);
        }, primary: true);
        center.Children.Add(coverTitle);
        Gap(center);
        center.Children.Add(coverDetail);
        Gap(center, 18);
        center.Children.Add(coverButton);
        cover.Children.Add(center);
        boardArea.Children.Add(cover);
        At(play, boardArea, 1, 0);

        var bottom = Stack();
        bottom.Margin = new Thickness(0, 14, 0, 0);
        progress = Text("", 13, "Muted");
        progressBar = new ProgressBar { Minimum = 0, Maximum = 81, Height = 5, Margin = new Thickness(0, 7, 0, 0),
            BorderThickness = new Thickness(0) };
        progressBar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        progressBar.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        bottom.Children.Add(progress);
        bottom.Children.Add(progressBar);
        At(play, bottom, 2, 0);
        playCard = Card(play);
        At(root, playCard, 1, 0);

        var side = Stack();
        side.Children.Add(Text("Нова гра", 19, "Text", true));
        Gap(side, 6);
        var levelGrid = new UniformGrid { Columns = 2 };
        for (int i = 0; i < 4; i++)
        {
            var d = (Difficulty)i;
            levels[i] = Action(Level(d), () => {
                state.NextDifficulty = nextDifficulty = d;
                Render();
                Save();
            }, "Обрати складність наступної гри");
            levels[i].Margin = new Thickness(2, 2, 2, 3);
            levelGrid.Children.Add(levels[i]);
        }
        side.Children.Add(levelGrid);
        newGame = Action("Нова головоломка", () => _ = Generate(true), "Ctrl+N", true);
        side.Children.Add(newGame);
        Gap(side, 7);
        side.Children.Add(Text("Цифри та нотатки", 17, "Text", true));
        Gap(side, 5);
        var pad = new UniformGrid { Columns = 3 };
        for (int i = 0; i < 9; i++)
        {
            int d = i + 1;
            digits[i] = Action(d.ToString(), () => Enter(d), $"Клавіша {d}");
            digits[i].Margin = new Thickness(2);
            digits[i].Padding = new Thickness(3, 4, 3, 4);
            digits[i].MinHeight = 39;
            pad.Children.Add(digits[i]);
        }
        side.Children.Add(pad);
        Gap(side, 8);
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
        { b.Margin = new Thickness(2);
        b.MinHeight = 32;
        b.Padding = new Thickness(6, 4, 6, 4);
        actions.Children.Add(b); }
        side.Children.Add(actions);
        Gap(side, 10);
        quest = Text("", 12, "Muted");
        rewardLife = Action("+1 життя · 8 с", () => Reward("life"));
        rewardHint = Action("+1 підказка", () => _ = ChooseResource("hint"));
        rewardCandidates = Action("+1 кандидати", () => _ = ChooseResource("candidates"));
        foreach (var b in new[] { rewardHint, rewardCandidates }) side.Children.Add(b);
        Gap(side, 10);
        side.Children.Add(Text("Налаштування", 16, "Text", true));
        autoFinish = Option("Автозавершення ≤9 клітинок", state.AutoFinish, v =>
        {
            state.AutoFinish = v;
            if (G is { Completed: false, OutOfLives: false } g)
            {
                g.AutoFinish = v;
                if (Playing) Change(x => x.TryAutoFinish());
            }
        });
        side.Children.Add(autoFinish);
        side.Children.Add(Option("Підсвічувати пов’язані клітинки", state.HighlightPeers, v => state.HighlightPeers = v));
        side.Children.Add(Option("Автоматично очищати нотатки", state.AutoClean, v => state.AutoClean = v));
        Gap(side, 9);
        stats = Text("", 12, "Muted");
        side.Children.Add(stats);
        var scroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        sideCard = Card(scroll, 13);
        At(root, sideCard, 1, 2);
        var result = Stack();
        result.MaxWidth = 680;
        resultTitle = Text("", 30, "Accent", true);
        resultTitle.TextAlignment = TextAlignment.Center;
        resultSummary = Text("", 19, "Text", true);
        resultSummary.TextAlignment = TextAlignment.Center;
        resultBreakdown = Text("", 15);
        resultComparison = Text("", 14, "Muted");
        result.Children.Add(resultTitle);
        Gap(result);
        result.Children.Add(resultSummary);
        Gap(result, 18);
        result.Children.Add(Text("Розклад оцінки", 18, "Text", true));
        Gap(result, 5);
        result.Children.Add(resultBreakdown);
        Gap(result, 15);
        result.Children.Add(Text("Порівняння з гравцями цієї складності", 18, "Text", true));
        Gap(result, 5);
        result.Children.Add(resultComparison);
        Gap(result, 15);
        var resultLevels = new UniformGrid { Columns = 4 };
        result.Children.Add(resultLevels);
        foreach (var level in Enum.GetValues<Difficulty > ())
        {
            var d = level;
            var button = Action(Level(d), () => {
                state.NextDifficulty = nextDifficulty = d;
                _ = Generate(false);
            });
            button.Margin = new Thickness(3);
            resultLevels.Children.Add(button);
        }
        result.Children.Add(Action("Нова головоломка", () => _ = Generate(false), primary: true));
        result.Children.Add(Action("Рейтинг", () => _ = ShowRanking()));
        resultDaily = Action("Щоденні нагороди", () => {
            weeklyRewards = challengeRewards = false;
            _ = ShowDaily();
        });
        result.Children.Add(resultDaily);
        resultCard = Card(new ScrollViewer { Content = result, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        resultCard.Visibility = Visibility.Collapsed;
        At(root, resultCard, 1, 0, 3);
        var menu = Stack();
        menu.MaxWidth = 580;
        menu.VerticalAlignment = VerticalAlignment.Center;
        var menuTitle = Text("Головне меню", 30, "Text", true);
        menuTitle.TextAlignment = TextAlignment.Center;
        menu.Children.Add(menuTitle);
        Gap(menu, 20);
        continueMenu = Action("Продовжити гру", () => {
            inMenu = false;
            Render();
            FocusCell();
        }, primary: true);
        menu.Children.Add(continueMenu);
        var menuLevels = new UniformGrid { Columns = 2 };
        menu.Children.Add(menuLevels);
        foreach (var d in Enum.GetValues<Difficulty > ())
        {
            var b = Action("Нова гра · " + Level(d), () => {
                state.NextDifficulty = nextDifficulty = d;
                _ = Generate(true);
            });
            b.Margin = new Thickness(3);
            menuLevels.Children.Add(b);
        }
        Gap(menu);
        var menuActions = new Grid();
        menuActions.ColumnDefinitions.Add(new ColumnDefinition());
        menuActions.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) menuActions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int menuIndex = 0;
        menu.Children.Add(menuActions);
        foreach (var b in new[] { Action("Щоденні ігри", () => _ = ShowChallenges()), Action("Тижневий турнір", () => _ = ShowWeekly()),
            Action("Рейтинг", () => _ = ShowRanking()), Action("Історія та статистика", ShowHistory),
            Action("Друзі та пошук", () => _ = ShowFriends()) })
        { b.Margin = new Thickness(3);
        At(menuActions, b, menuIndex / 2, menuIndex % 2, menuIndex == 4 ? 2 : 1);
            menuIndex++; }
        menuCard = Card(new ScrollViewer { Content = menu, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        At(root, menuCard, 1, 0, 3);
        var footer = new Grid { Margin = new Thickness(3, 10, 3, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status = Text("", 12, "Muted");
        saveStatus = Text("", 12, "Muted");
        saveStatus.Margin = new Thickness(12, 0, 0, 0);
        At(footer, status, 0, 0);
        At(footer, saveStatus, 0, 1);
        At(root, footer, 2, 0, 3);
        mainContent = root;
        surfaceHost = new Grid { ClipToBounds = true };
        surfaceHost.Children.Add(mainContent);
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition());
        At(shell, header, 0, 0);
        At(shell, surfaceHost, 1, 0);
        Content = shell;
    }

    private StackPanel Metric(string label, TextBlock value)
    {
        var s = Stack();
        s.Children.Add(Text(label, 11, "Muted", true));
        s.Children.Add(value);
        return s;
    }
    private CheckBox Option(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            IsChecked = value };
        c.Checked += (_, _) => {
            set(true);
            Render();
            Save();
        };
        c.Unchecked += (_, _) => {
            set(false);
            Render();
            Save();
        };
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
        border.AppendChild(presenter);
        template.VisualTree = border;
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
            b.GotKeyboardFocus += (_, _) => {
                if (Playing && G!.Selected != index) Select(index, false);
            };
            cells[i] = b;
            At(result, b, r, c);
        }
        return result;
    }
}
