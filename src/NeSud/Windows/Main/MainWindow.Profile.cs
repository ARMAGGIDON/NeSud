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
    private async Task ShowProfile(string? user)
    {
        var panel = Stack();
        var title = Text("Профіль", 24, "Text", true);
        panel.Children.Add(title);
        var filter = new ComboBox { ItemsSource = new[] { "Усі складності", "Легка", "Середня", "Складна", "Експерт" }, SelectedIndex = 0 };
        panel.Children.Add(filter);
        var body = Stack();
        panel.Children.Add(body);
        var window = Page("Профіль", panel, 690, 750);
        int page = 0;
        bool loading = false;
        var nav = new UniformGrid { Columns = 2 };
        panel.Children.Add(nav);
        var prev = Action("Попередня");
        var next = Action("Наступна");
        prev.Click += async (_, _) => {
            page--;
            await Load();
        };
        next.Click += async (_, _) => {
            page++;
            await Load();
        };
        if (user is null && account is not null)
            panel.Children.Add(Action("Налаштування акаунта", () => { window.Close(); ShowAccount(); }));
        nav.Children.Add(prev);
        nav.Children.Add(next);
        async Task Load()
        {
            if (loading)return;
            loading = true;
            prev.IsEnabled = next.IsEnabled = false;
            try
            {
                var p = await Request<PublicProfile > ("profile?page=" + page + (user is null?"": "&user=" + Uri.EscapeDataString(user)) + (filter.SelectedIndex > 0?"&difficulty=" + (Difficulty)(filter.SelectedIndex - 1): ""));
                if (!window.IsVisible)return;
                title.Text = p.User;
                body.Children.Clear();
                body.Children.Add(Text($"Ігор: {p.Games} • перемог: {p.Wins} • ідеальних: {p.PerfectWins} • балів: {p.Score:N0}", 15));
                body.Children.Add(Text($"Серія: {p.CurrentStreak}/{p.BestStreak} • щоденних перемог: {p.DailyWins}", 14, "Muted"));
                Gap(body);
                foreach (var s in p.ByDifficulty ?? [])body.Children.Add(Text($"{Level(s.Difficulty)}: {s.Wins}/{s.Games} • ідеальні {s.PerfectWins} • серія {s.CurrentStreak}/{s.BestStreak} • {s.Score:N0} балів", 14));
                if (account is not null && user is not null)
                {
                    var links = await Request<List<FriendEntry>>("friends");
                    if (!window.IsVisible)return;
                    var link = links.FirstOrDefault(x => x.User == p.User);
                    AddFriendActions(body, p.User, link?.Status, async() => await Load());
                }
                body.Children.Add(Text("Місячні кубки", 18, "Text", true));
                var cups = new WrapPanel();
                body.Children.Add(cups);
                foreach (var month in p.Cups)
                {
                    var cup = Card(Text("🏆 " + month, 18, "Accent", true), 8);
                    cup.Margin = new Thickness(3);
                    cups.Children.Add(cup);
                }
                if (p.Cups.Count == 0)body.Children.Add(Text("Кубків ще немає.", 13, "Muted"));
                Gap(body);
                body.Children.Add(Text("Історія ігор", 18, "Text", true));
                foreach (var r in p.History)
                {
                    string outcome = r.Outcome switch { "completed" => "Перемога", "defeated" => "Поразка", _ => "Перервано" };
                    body.Children.Add(Text($"{r.Ended.ToLocalTime():g} • {(r.ChallengeDay is null?r.Source:"Щоденна "+r.ChallengeDay)} • {Level(r.Difficulty)} • {outcome} • {FormatTime(r.Seconds)} • {r.Score} балів", 13));
                }
                if (p.History.Count == 0)body.Children.Add(Text("Ігор ще немає.", 13, "Muted"));
                prev.IsEnabled = page > 0;
                next.IsEnabled = p.HasMore;
            }
            catch (Exception ex)
            {
                body.Children.Clear();
                body.Children.Add(Text(ex.Message, 14, "Wrong"));
                prev.IsEnabled = page > 0;
            }
            finally
            {
                loading = false;
            }
        }
        filter.SelectionChanged += async (_, _) => {
            page = 0;
            await Load();
        };
        window.Loaded += async (_, _) => await Load();
        window.ShowDialog();
        await Task.CompletedTask;
    }
}
