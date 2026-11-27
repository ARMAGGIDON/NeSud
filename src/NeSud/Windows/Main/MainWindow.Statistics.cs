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
    private async Task ShowRanking()
    {
        var panel = Stack();
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(Text("Рейтинг", 24, "Text", true));
        var indicator = ConnectionIndicator();
        indicator.Margin = new Thickness(8,0,0,0);
        heading.Children.Add(indicator);
        panel.Children.Add(heading);
        panel.Children.Add(Text("Серверні результати та загальні бали з офлайн-іграми.", 13, "Muted"));
        DateTimeOffset? updatedAt = null;
        Gap(panel);
        var metric = new ComboBox { Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line"), ItemsSource = new[] { "Бали", "Перемоги", "Ідеальні перемоги", "Поточна серія", "Найдовша серія" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        var mode = new ComboBox
        {
            ItemsSource = new[] { "Серверний рейтинг", "Загальний рейтинг (офлайн включено)" },
            SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 0)
        };
        var filters = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        var modePanel = Stack();
        modePanel.Children.Add(Text("Таблиця", 12, "Muted"));
        modePanel.Children.Add(mode);
        filters.Children.Add(modePanel);
        var metricPanel = Stack();
        metricPanel.Children.Add(Text("Показник", 12, "Muted"));
        metric.Margin = new Thickness(0, 0, 8, 0);
        metricPanel.Children.Add(metric);
        filters.Children.Add(metricPanel);
        var levelFilter = new ComboBox { ItemsSource = new[] { "Усі складності", "Легка", "Середня", "Складна", "Експерт" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        var levelPanel = Stack();
        levelPanel.Children.Add(Text("Складність", 12, "Muted"));
        levelPanel.Children.Add(levelFilter);
        filters.Children.Add(levelPanel);
        panel.Children.Add(filters);
        var rows = Stack();
        panel.Children.Add(rows);
        Gap(panel);
        var window = Page("Рейтинг", panel, 560);
        window.Reopen = () => _ = ShowRanking();
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
                int selectedMode = mode.SelectedIndex;
                var ranks = await Request<List<AccumulatedRank>>("accumulated?metric=" + key + filter +
                    (selectedMode == 1 ? "&all=true" : ""), token: "");
                if (!window.IsVisible) return;
                rows.Children.Clear();
                void Table(string title, List<AccumulatedRank> data)
                {
                    rows.Children.Add(Text(title, 18, "Text", true));
                    Gap(rows, 8);
                    AddPlayerListHeading(rows, "Перемоги / ідеальні", "Серія / рекорд", "Бали");
                    for (int i = 0; i < data.Count; i++)
                    {
                        var r = data[i];
                        AddPlayerRow(rows, r.User, i + 1, $"{r.Wins} / {r.PerfectWins}", $"{r.CurrentStreak} / {r.BestStreak}", r.Score.ToString("N0"));
                        Gap(rows, 5);
                    }
                    if (data.Count == 0) rows.Children.Add(Text("Ще немає результатів.", 13, "Muted"));
                    Gap(rows, 16);
                }
                Table(selectedMode == 1 ? "Загальний рейтинг" : "Серверний рейтинг", ranks);
                updatedAt = DateTimeOffset.Now;
                SetConnectionIndicator(indicator, true, updatedAt);
            }
            catch
            {
                if (window.IsVisible) SetConnectionIndicator(indicator, false, updatedAt, "Показані дані можуть бути застарілими.");
            }
            finally
            {
                loading = false;
            }
        }
        mode.SelectionChanged += async (_, _) => await Reload();
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
        window.Reopen = () => _ = ShowWeekly();
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
                    AddPlayerListHeading(body, "Перемоги", "Бали");
                    for (int i = 0; i<leaders.Count; i++)
                    {
                        var r = leaders[i];
                        AddPlayerRow(body, r.User, i + 1, r.Games.ToString("N0"), r.Score.ToString("N0"));
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
