using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private System.Windows.Shapes.Path connectionIndicator = null!;
    private DateTimeOffset? lastConnectionResponse;

    private System.Windows.Shapes.Path ConnectionIndicator()
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,8 Q12,0 21,8 M6,12 Q12,6 18,12 M9,16 Q12,13 15,16 M12,20 L12.01,20"),
            Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(6,0,6,0), VerticalAlignment = VerticalAlignment.Center,
            Focusable = true, ToolTip = "Підключення"
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        return icon;
    }

    private void SetConnectionIndicator(System.Windows.Shapes.Path icon, bool online, DateTimeOffset? updated, string? extra = null)
    {
        if (online) icon.Stroke = ColorBrush("#4CB884");
        else icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MutedBrush");
        string tooltip = online ? "Онлайн" : "Офлайн";
        if (updated is { } at) tooltip += "\nОстання відповідь: " + at.ToLocalTime().ToString("g");
        if (extra is not null) tooltip += "\n" + extra;
        icon.ToolTip = tooltip;
        System.Windows.Automation.AutomationProperties.SetName(icon, tooltip);
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
                        window.Close();
                        if (local) ResumeLocalGame();
                        else _ = ShowChallengeGame(r.ChallengeDay!);
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
