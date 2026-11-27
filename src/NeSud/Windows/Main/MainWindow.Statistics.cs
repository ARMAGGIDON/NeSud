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
        panel.Children.Add(Text("Рейтинг", 24, "Text", true));
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
        int requestVersion = 0;
        bool loading = false;
        string? shownFilter = null, shownSignature = null;
        var status = Stack();
        panel.Children.Insert(panel.Children.IndexOf(rows), status);
        async Task Reload()
        {
            if (!window.IsVisible) return;
            int version = ++requestVersion;
            loading = true;
            try
            {
                string key = new[] { "score", "wins", "perfect", "current", "streak" }[Math.Max(0, metric.SelectedIndex)];
                string filter = levelFilter.SelectedIndex > 0?"&difficulty=" + (Difficulty)(levelFilter.SelectedIndex - 1): "";
                int selectedMode = mode.SelectedIndex;
                string currentFilter = $"{selectedMode}:{key}:{filter}";
                if (shownFilter != currentFilter)
                {
                    rows.Children.Clear();
                    shownFilter = currentFilter;
                    shownSignature = null;
                }
                if (rows.Children.Count == 0) SetDetailLoading(status, "Завантаження рейтингу…");
                var ranks = await Request<List<AccumulatedRank>>("accumulated?metric=" + key + filter +
                    (selectedMode == 1 ? "&all=true" : ""), token: "");
                if (!window.IsVisible || version != requestVersion) return;
                string signature = System.Text.Json.JsonSerializer.Serialize(ranks);
                status.Children.Clear();
                if (signature == shownSignature) return;
                rows.Children.Clear();
                int activeMetric = key == "score" ? 2 : key is "wins" or "perfect" ? 0 : 1;
                void Table(List<AccumulatedRank> data)
                {
                    AddPlayerListHeading(rows, activeMetric, "Перемоги / ідеальні", "Серія / рекорд", "Бали");
                    for (int i = 0; i < data.Count; i++)
                    {
                        var r = data[i];
                        AddPlayerRow(rows, r.User, i + 1, activeMetric, $"{r.Wins} / {r.PerfectWins}", $"{r.CurrentStreak} / {r.BestStreak}", r.Score.ToString("N0"));
                        Gap(rows, 5);
                    }
                    if (data.Count == 0) rows.Children.Add(Text("Ще немає результатів.", 13, "Muted"));
                    Gap(rows, 16);
                }
                Table(ranks);
                shownSignature = signature;
            }
            catch
            {
                if (window.IsVisible && version == requestVersion)
                {
                    SetDetailMessage(status, rows.Children.Count == 0 ? "Не вдалося завантажити рейтинг." : "Не вдалося оновити рейтинг.");
                    return;
                }
            }
            finally
            {
                if (version == requestVersion)
                {
                    loading = false;
                    if (status.Children.Count > 0 && status.Children[0] is Border) status.Children.Clear();
                }
            }
        }
        mode.SelectionChanged += async (_, _) => await Reload();
        metric.SelectionChanged += async (_, _) => await Reload();
        levelFilter.SelectionChanged += async (_, _) => await Reload();
        refresh.Tick += async (_, _) => { if (!loading) await Reload(); };
        window.Loaded += async (_, _) => {
            refresh.Start();
            await Reload();
        };
        window.Closed += (_, _) => { requestVersion++; refresh.Stop(); };
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
            SetDetailLoading(body, "Завантаження турніру…");
            try
            {
                var w = await Request<WeeklyRanking > ("weekly", token: "");
                if (!window.IsVisible) return;
                body.Children.Clear();
                void Table(string title, List<RankEntry> leaders)
                {
                    body.Children.Add(Text(title, 18, "Text", true));
                    Gap(body);
                    AddPlayerListHeading(body, 1, "Перемоги", "Бали");
                    for (int i = 0; i<leaders.Count; i++)
                    {
                        var r = leaders[i];
                        AddPlayerRow(body, r.User, i + 1, 1, r.Games.ToString("N0"), r.Score.ToString("N0"));
                    }
                    if (leaders.Count == 0) body.Children.Add(Text("Немає результатів.", 13, "Muted"));
                    Gap(body, 12);
                }
                Table("Тиждень від " + w.Week, w.Leaders);
                Table(w.PreviousWeek is null ? "Попередній тиждень" : "Тиждень від " + w.PreviousWeek, w.PreviousLeaders);
                if (account is not null)
                {
                    daily = await Request<DailyStatus > ("daily");
                    prizes.Visibility = daily.Packs.Any(p => p.Milestone == "weekly")?Visibility.Visible: Visibility.Collapsed;
                }
            }
            catch
            {
                if (window.IsVisible)
                {
                    if (body.Children.Count == 1 && body.Children[0] is Border) body.Children.Clear();
                    body.Children.Add(Text("Сервіс недоступний.", 14, "Wrong"));
                }
            }
        };
        window.ShowDialog();
        await Task.CompletedTask;
    }
}
