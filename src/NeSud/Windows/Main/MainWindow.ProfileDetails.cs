using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private Button connectionIndicator = null!;
    private DateTimeOffset? lastConnectionResponse;

    private sealed record ConnectionStatusView(Ellipse Dot, Ellipse Ring, TextBlock Heading,
        TextBlock Detail, Popup Popup)
    {
        public bool? Online { get; set; }
        public string? Message { get; set; }
    }

    private Button ConnectionIndicator()
    {
        var scale = new ScaleTransform(1, 1);
        var ring = new Ellipse
        {
            Width = 20, Height = 20, StrokeThickness = 1.5, Opacity = .32,
            RenderTransform = scale, RenderTransformOrigin = new Point(.5, .5)
        };
        var dot = new Ellipse { Width = 9, Height = 9 };
        ring.SetResourceReference(Shape.StrokeProperty, "MutedBrush");
        dot.SetResourceReference(Shape.FillProperty, "MutedBrush");
        var content = new Grid { Width = 28, Height = 28, IsHitTestVisible = false };
        foreach (var element in new FrameworkElement[] { ring, dot })
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(element);
        }
        var button = Action("", tip: "Стан з’єднання");
        button.Content = content;
        button.Width = 46;
        button.MinHeight = 46;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center;

        var title = Text("Підключення", 15, "Text", true);
        var detail = Text("Очікуємо відповідь сервера.", 13, "Muted");
        var body = Stack();
        body.Children.Add(title);
        Gap(body, 5);
        body.Children.Add(detail);
        var card = Card(body, 12);
        card.MinWidth = 210;
        card.MaxWidth = 300;
        card.Focusable = true;
        var popup = new Popup
        {
            PlacementTarget = button, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (size, target, _) =>
            [
                new CustomPopupPlacement(new Point((target.Width - size.Width) / 2, target.Height + 8), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point((target.Width - size.Width) / 2, -size.Height - 8), PopupPrimaryAxis.Horizontal)
            ],
            Child = card, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None
        };
        button.Tag = new ConnectionStatusView(dot, ring, title, detail, popup);
        button.Click += (_, _) => popup.IsOpen = true;
        popup.Opened += (_, _) =>
        {
            ToolTipService.SetIsEnabled(button, false);
            button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            Hover();
            card.Focus();
        };
        popup.Closed += (_, _) =>
        {
            button.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
            ToolTipService.SetIsEnabled(button, true);
            Hover();
        };
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            button.Focus();
            e.Handled = true;
        };
        button.Unloaded += (_, _) => popup.IsOpen = false;
        button.IsEnabledChanged += (_, _) => { if (!button.IsEnabled) popup.IsOpen = false; };

        void Hover()
        {
            bool active = button.IsMouseOver || button.IsKeyboardFocused || popup.IsOpen;
            button.SetResourceReference(Control.BackgroundProperty, active ? "PeerBrush" : "ControlBrush");
            double opacity = active ? .8 : .32;
            double size = active ? 1.13 : 1;
            if (SystemParameters.ClientAreaAnimation)
            {
                var duration = TimeSpan.FromMilliseconds(160);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(size, duration));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(size, duration));
                ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, duration));
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ring.BeginAnimation(UIElement.OpacityProperty, null);
                scale.ScaleX = scale.ScaleY = 1;
                ring.Opacity = opacity;
            }
        }
        button.MouseEnter += (_, _) => Hover();
        button.MouseLeave += (_, _) => Hover();
        button.IsKeyboardFocusedChanged += (_, _) => Hover();
        return button;
    }

    private void CloseConnectionPopup()
    {
        if (connectionIndicator?.Tag is ConnectionStatusView view) view.Popup.IsOpen = false;
    }

    private void SetConnectionIndicator(Button button, bool online, DateTimeOffset? updated, string? extra = null)
    {
        if (button.Tag is not ConnectionStatusView view) return;
        string title = online ? "Онлайн" : "Офлайн";
        string detail = updated is { } at ? "Остання відповідь: " + at.ToLocalTime().ToString("g")
            : "Ще немає відповіді сервера.";
        if (extra is not null) detail += "\n" + extra;
        if (view.Online != online)
        {
            string color = online ? "OnlineBrush" : "MutedBrush";
            view.Dot.SetResourceReference(Shape.FillProperty, color);
            view.Ring.SetResourceReference(Shape.StrokeProperty, color);
            view.Heading.SetResourceReference(TextBlock.ForegroundProperty, color);
            view.Heading.Text = title;
            view.Online = online;
        }
        string message = title + "\n" + detail;
        if (view.Message == message) return;
        view.Message = message;
        view.Detail.Text = detail;
        button.ToolTip = "Стан з’єднання: " + title + ". Натисніть для деталей.";
        System.Windows.Automation.AutomationProperties.SetName(button, "Стан з’єднання: " + title);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, detail);
    }

    private static string ProfileTime(long seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

    private (PublicProfile, OwnProfileDetails) LocalProfile(string scope, int page, int selectedLevel)
    {
        var records = store.History(scope).OrderBy(r => r.Ended).ThenBy(r => r.Id).ToList();
        static LevelStats Summary(Difficulty d, IEnumerable<GameRecord> source)
        {
            int games = 0, wins = 0, perfect = 0, current = 0, best = 0;
            long score = 0;
            int? seconds = null;
            foreach (var r in source)
            {
                games++; score += r.Score;
                if (r.Outcome != "completed") { current = 0; continue; }
                wins++; best = Math.Max(best, ++current);
                var g = r.Game;
                if (g.Mistakes == 0 && g.Hints == 0 && g.CandidateUses == 0 && g.AutoFilled == 0 && g.LifeRestores == 0) perfect++;
                seconds = Math.Min(seconds ?? int.MaxValue, g.Seconds);
            }
            return new(d, games, wins, perfect, current, best, score, seconds);
        }
        var totals = Summary(Difficulty.Easy, records);
        var levels = Enum.GetValues<Difficulty>().Select(d => Summary(d, records.Where(r => r.Game.Puzzle.Difficulty == d))).ToList();
        var history = records.AsEnumerable().Reverse().Where(r => selectedLevel <= 0 || (int)r.Game.Puzzle.Difficulty == selectedLevel - 1)
            .Skip(page * 50).Take(51).Select(r => new PublicGame(r.Ended, r.ChallengeDay is null ? "regular" : "challenge",
                r.Source, r.Game.Puzzle.Difficulty, r.Outcome, r.Game.Seconds, r.Score, r.Game.Mistakes, r.Game.Hints,
                r.Game.CandidateUses, r.Game.AutoFilled, r.Game.LifeRestores, r.ChallengeDay)).ToList();
        bool more = history.Count > 50;
        if (more) history.RemoveAt(50);
        var profile = new PublicProfile(account?.Username ?? account?.User ?? "Локальний профіль", totals.Games, totals.Wins,
            totals.PerfectWins, totals.CurrentStreak, totals.BestStreak, totals.Score,
            records.Count(r => r.ChallengeDay is not null && r.Outcome == "completed"), [], history, more, levels);
        var details = new OwnProfileDetails(records.Sum(r => (long)r.Game.Seconds), records.Sum(r => (long)r.Game.Mistakes),
            records.Sum(r => (long)r.Game.Hints), []);
        return (profile, details);
    }

    private void AddActiveGames(StackPanel panel, List<ActiveGameInfo> serverGames, InAppSurface window)
    {
        var active = serverGames.ToList();
        if (G is { Completed: false } g && !g.FinalDefeat(DateTimeOffset.UtcNow) && (state.GameScope ?? "") == Scope)
        {
            active.RemoveAll(r => r.Id == state.GameId);
            active.Insert(0, new(state.GameId, "desktop", null, g.Puzzle.Difficulty, g.Seconds, DateTimeOffset.Now, g.Paused));
        }
        panel.Children.Add(Text("Активні ігри", 18, "Text", true));
        if (active.Count == 0) panel.Children.Add(Text("Активних ігор немає.", 13, "Muted"));
        else
        {
            var table = ProfileTable(1.5, 1, 1, 1);
            AddProfileRow(table, true, "Режим / оновлено", "Складність", "Стан / час", "Дія");
            foreach (var r in active)
            {
                bool local = r.Source == "desktop" && r.Id == state.GameId && (state.GameScope ?? "") == Scope;
                bool daily = r.ChallengeDay is not null;
                string mode = daily ? "Щоденна " + r.ChallengeDay : r.Source == "desktop" ? "Desktop" : "Web";
                AddProfileRow(table, false, mode + "\n" + r.Updated.ToLocalTime().ToString("g"), Level(r.Difficulty),
                    (r.Paused ? "На паузі" : "Розпочато") + "\n" + FormatTime(r.Seconds), local || daily ? "" : r.Source == "web" ? "У браузері" : "На іншому пристрої");
                if (local || daily)
                {
                    var button = Action("Продовжити", () =>
                    {
                        if (!window.IsVisible || surfaces.LastOrDefault() != window) return;
                        var secret = account?.Token;
                        string scope = Scope;
                        NavigateHomeThen(() =>
                        {
                            if (secret != account?.Token || scope != Scope) return;
                            if (local && state.GameId == r.Id) ResumeLocalGame();
                            else if (daily) _ = ShowChallengeGame(r.ChallengeDay!, resumeOnly: true);
                        });
                    });
                    button.Margin = new Thickness(4);
                    At(table, button, table.RowDefinitions.Count - 1, 3);
                }
            }
            panel.Children.Add(table);
        }
        Gap(panel, 16);
    }
}
