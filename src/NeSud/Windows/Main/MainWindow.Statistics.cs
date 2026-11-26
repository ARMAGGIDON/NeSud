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
    private void ShowHistory()
    {
        var records = store.History(Scope);
        var panel = Stack();
        panel.Children.Add(Text("Історія та статистика", 24, "Text", true));
        Gap(panel);
        var wins = records.Where(r => r.Outcome == "completed").ToList();
        int streak = 0, bestStreak = 0;
        foreach (var record in records.OrderBy(r => r.Ended).ThenBy(r => r.Id))
        {
            if (record.Outcome == "completed")
            {
                streak++;
                bestStreak = Math.Max(bestStreak, streak);
            }
            else streak = 0;
        }
        int perfect = wins.Count(r => r.Game.Mistakes == 0 && r.Game.Hints == 0 && r.Game.CandidateUses == 0 && r.Game.AutoFilled == 0 && r.Game.LifeRestores == 0);
        panel.Children.Add(Text($"Ідеальні перемоги: {perfect} • серія: {streak} • рекорд серії: {bestStreak}", 14, "Accent"));
        panel.Children.Add(Text($"Ігор: {records.Count}   Перемог: {wins.Count}   Балів: {wins.Sum(r => (long)r.Score)}", 16));
        panel.Children.Add(Text($"Час: {FormatTime(records.Sum(r => r.Game.Seconds))} • Помилки: {records.Sum(r => (long)r.Game.Mistakes)} • Підказки: {records.Sum(r => (long)r.Game.Hints)}", 13, "Muted"));
        foreach (var d in Enum.GetValues<Difficulty > ())
        {
            var games = records.Where(r => r.Game.Puzzle.Difficulty == d).ToList();
            var done = games.Where(r => r.Outcome == "completed").ToList();
            string best = done.Count == 0 ? "немає" : FormatTime(done.Min(r => r.Game.Seconds));
            int run = 0, record = 0;
            foreach (var entry in games.OrderBy(r => r.Ended).ThenBy(r => r.Id))
            {
                run = entry.Outcome == "completed"?run + 1: 0;
                record = Math.Max(record, run);
            }
            int clean = done.Count(r => r.Game.Mistakes == 0 && r.Game.Hints == 0 && r.Game.CandidateUses == 0 && r.Game.AutoFilled == 0 && r.Game.LifeRestores == 0);
            panel.Children.Add(Text($"{Level(d)}: {done.Count}/{games.Count} перемог • ідеальні {clean} • серія {run}/{record} • бали {done.Sum(r=>(long)r.Score)} • найкращий час: {best}", 13, "Muted"));
        }
        Gap(panel, 12);
        if (G is { Completed: false } && !G.FinalDefeat(DateTimeOffset.UtcNow) && (state.GameScope ?? "") == Scope)
            panel.Children.Add(Text($"Поточна гра: {Level(G.Puzzle.Difficulty)} • {FormatTime(G.Seconds)} • у процесі", 14, "Accent", true));
        var list = Stack();
        foreach (var r in records)
        {
            var item = Stack();
            item.Children.Add(Text($"{Level(r.Game.Puzzle.Difficulty)} • {(r.Outcome == "completed" ? "Перемога" : r.Outcome == "defeated" ? "Поразка" : "Перервано")} • {r.Score} балів", 15, "Text", true));
            item.Children.Add(Text($"{r.Ended.ToLocalTime():g} • {(r.ChallengeDay is null ? r.Source : "Щоденна " + r.ChallengeDay)} • {FormatTime(r.Game.Seconds)} • помилки: {r.Game.Mistakes} • підказки: {r.Game.Hints} • кандидати: {r.Game.CandidateUses} • відновлення життів: {r.Game.LifeRestores}", 12, "Muted"));
            var card = Card(item, 10);
            card.Margin = new Thickness(0, 0, 0, 6);
            list.Children.Add(card);
        }
        if (records.Count == 0) list.Children.Add(Text("Завершених або перерваних ігор ще немає.", 14, "Muted"));
        panel.Children.Add(list);
        Gap(panel);
        var window = Page("Історія", panel, 680, 720);
        window.ShowDialog();
    }

    private async Task ShowRanking()
    {
        var panel = Stack();
        panel.Children.Add(Text("Рейтинг", 24, "Text", true));
        panel.Children.Add(Text("Серверні результати та загальні бали з офлайн-іграми.", 13, "Muted"));
        var indicator = Text("Завантаження…", 13, "Muted");
        panel.Children.Add(indicator);
        Gap(panel);
        var metric = new ComboBox { Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line"), ItemsSource = new[] { "Бали", "Перемоги", "Ідеальні перемоги", "Поточна серія", "Найдовша серія" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(metric);
        var levelFilter = new ComboBox { ItemsSource = new[] { "Усі складності", "Легка", "Середня", "Складна", "Експерт" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(levelFilter);
        var rows = Stack();
        panel.Children.Add(rows);
        Gap(panel);
        var window = Page("Рейтинг", panel, 560);
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        bool loading = false;
        async Task Reload()
        {
            if (loading || !window.IsVisible) return;
            loading = true;
            try
            {
                string key = new[] { "score", "wins", "perfect", "current", "streak" }[Math.Max(0, metric.SelectedIndex)];
                string filter = levelFilter.SelectedIndex > 0?"&difficulty=" + (Difficulty)(levelFilter.SelectedIndex - 1): "";
                var ranks = await Request<List<AccumulatedRank>>("accumulated?metric=" + key + filter, token: "");
                var allRanks = await Request<List<AccumulatedRank>>("accumulated?metric=" + key + "&all=true" + filter, token: "");
                if (!window.IsVisible) return;
                rows.Children.Clear();
                void Table(string title, List<AccumulatedRank> data)
                {
                    rows.Children.Add(Text(title, 18, "Text", true));
                    Gap(rows, 8);
                    for (int i = 0; i < data.Count; i++)
                    {
                        var r = data[i];
                        rows.Children.Add(Action($"{i + 1}. {r.User} • {r.Wins} перемог • {r.PerfectWins} ідеальних • серія {r.CurrentStreak}/{r.BestStreak} • {r.Score} балів", () => _ = ShowProfile(r.User)));
                        Gap(rows, 5);
                    }
                    if (data.Count == 0) rows.Children.Add(Text("Ще немає результатів.", 13, "Muted"));
                    Gap(rows, 16);
                }
                Table("Серверний рейтинг", ranks);
                Table("Загальні бали (офлайн включено)", allRanks);
                indicator.Text = "● Онлайн • оновлено " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch
            {
                if (window.IsVisible) indicator.Text = "○ Офлайн • очікуємо відновлення зв’язку. Показані дані можуть бути застарілими.";
            }
            finally
            {
                loading = false;
            }
        }
        metric.SelectionChanged += async (_, _) => await Reload();
        levelFilter.SelectionChanged += async (_, _) => await Reload();
        refresh.Tick += async (_, _) => await Reload();
        window.Loaded += async (_, _) => {
            refresh.Start();
            await Reload();
        };
        window.Closed += (_, _) => refresh.Stop();
        window.ShowDialog();
        await Task.CompletedTask;
    }

    private async Task ShowWeekly()
    {
        var panel = Stack();
        panel.Children.Add(Text("Тижневий турнір", 24, "Text", true));
        var body = Stack();
        panel.Children.Add(body);
        var window = Page("Турнір", panel, 600);
        var prizes = Action("Отримати приз", () => {
            window.Close();
            weeklyRewards = true;
            challengeRewards = false;
            _ = ShowDaily();
        }, primary: true);
        prizes.Visibility = Visibility.Collapsed;
        panel.Children.Add(prizes);
        window.Loaded += async (_, _) =>
        {
            try
            {
                var w = await Request<WeeklyRanking > ("weekly", token: "");
                if (!window.IsVisible) return;
                void Table(string title, List<RankEntry> leaders)
                {
                    body.Children.Add(Text(title, 18, "Text", true));
                    Gap(body);
                    for (int i = 0; i<leaders.Count; i++)
                    {
                        var r = leaders[i];
                        body.Children.Add(Action($"{i+1}. {r.User} • {r.Games} перемог • {r.Score} балів", () => _ = ShowProfile(r.User)));
                    }
                    if (leaders.Count == 0) body.Children.Add(Text("Немає результатів.", 13, "Muted"));
                    Gap(body, 12);
                }
                Table(w.Week, w.Leaders);
                Table(w.PreviousWeek ?? "Попередній тиждень", w.PreviousLeaders);
                if (account is not null)
                {
                    daily = await Request<DailyStatus > ("daily");
                    prizes.Visibility = daily.Packs.Any(p => p.Milestone == "weekly")?Visibility.Visible: Visibility.Collapsed;
                }
            }
            catch
            {
                body.Children.Add(Text("Сервіс недоступний.", 14, "Wrong"));
            }
        };
        window.ShowDialog();
        await Task.CompletedTask;
    }
}
