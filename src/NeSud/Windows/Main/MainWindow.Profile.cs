using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private async Task ShowProfile(string? user)
    {
        var panel = Stack();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(header);
        var identity = Stack();
        identity.Margin = new Thickness(12, 0, 12, 0);
        identity.VerticalAlignment = VerticalAlignment.Center;
        At(header, identity, 0, 1);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        identity.Children.Add(title);
        var name = Text("Профіль", 24, "Text", true);
        title.Children.Add(name);
        var verified = VerifiedMark();
        verified.Visibility = Visibility.Collapsed;
        title.Children.Add(verified);
        var tools = Stack();
        tools.VerticalAlignment = VerticalAlignment.Center;
        At(header, tools, 0, 2);
        var friendActions = Stack();
        identity.Children.Add(friendActions);
        var avatarHost = new Grid();
        At(header, avatarHost, 0, 0);
        var body = Stack();
        panel.Children.Add(body);
        var historyTitle = Text("Історія ігор", 18, "Text", true);
        panel.Children.Add(historyTitle);
        Gap(panel, 6);
        var filter = new ComboBox { ItemsSource = new[] { "Усі складності", "Легка", "Середня", "Складна", "Експерт" }, SelectedIndex = 0 };
        panel.Children.Add(filter);
        var history = Stack();
        panel.Children.Add(history);
        var nav = new UniformGrid { Columns = 2 };
        panel.Children.Add(nav);
        var prev = Action("Попередня");
        var next = Action("Наступна");
        prev.Margin = new Thickness(0, 8, 4, 0);
        next.Margin = new Thickness(4, 8, 0, 0);
        nav.Children.Add(prev);
        nav.Children.Add(next);
        var window = Page("Профіль", panel, 900);
        int page = 0;
        bool loading = false;
        bool reloadPending = false;
        bool own = user is null && account is not null;
        string? sessionToken = account?.Token;

        async Task Load()
        {
            if (loading) { reloadPending = true; return; }
            loading = true;
            prev.IsEnabled = next.IsEnabled = false;
            try
            {
                var p = await Request<PublicProfile>("profile?page=" + page +
                    (user is null ? "" : "&user=" + Uri.EscapeDataString(user)) +
                    (filter.SelectedIndex > 0 ? "&difficulty=" + (Difficulty)(filter.SelectedIndex - 1) : ""));
                if (!window.IsVisible || sessionToken != account?.Token) return;
                name.Text = p.User;
                verified.Visibility = p.Verified ? Visibility.Visible : Visibility.Collapsed;
                avatarHost.Children.Clear();
                avatarHost.Children.Add(PlayerAvatar(p.User, 88, own && p.Verified ? async () =>
                {
                    try
                    {
                        await EditAvatar();
                        if (window.IsVisible) await Load();
                    }
                    catch (Exception ex) { if (window.IsVisible) Dialog("Аватар", ex.Message, false); }
                } : null));
                tools.Children.Clear();
                if (own)
                {
                    tools.Children.Add(Action(p.Verified ? "Налаштування профілю" : "Підтвердити пошту", async () =>
                    {
                        await EditOwnProfile();
                        if (window.IsVisible) await Load();
                    }));
                    if (p.Verified) tools.Children.Add(Action("Запрошення", async () => await ShowInvitations()));
                    tools.Children.Add(Action("Акаунт", () =>
                    {
                        ShowAccount();
                        if (account?.Token != sessionToken) window.Close();
                        else _ = Load();
                    }));
                }
                body.Children.Clear();
                body.Children.Add(Text("Статистика", 18, "Text", true));
                body.Children.Add(ProfileMetrics(
                    ("Ігор", p.Games.ToString("N0")), ("Перемог", p.Wins.ToString("N0")),
                    ("Ідеальних", p.PerfectWins.ToString("N0")), ("Балів", p.Score.ToString("N0")),
                    ("Серія / рекорд", $"{p.CurrentStreak} / {p.BestStreak}"), ("Щоденних перемог", p.DailyWins.ToString("N0"))));
                body.Children.Add(Text("За складностями", 18, "Text", true));
                var table = ProfileTable(1.2, 1.1, .8, 1.1, 1);
                AddProfileRow(table, true, "Складність", "Перемоги / ігри", "Ідеальні", "Серія / рекорд", "Бали");
                foreach (var s in p.ByDifficulty ?? [])
                    AddProfileRow(table, false, Level(s.Difficulty), $"{s.Wins} / {s.Games}", s.PerfectWins.ToString("N0"),
                        $"{s.CurrentStreak} / {s.BestStreak}", s.Score.ToString("N0"));
                body.Children.Add(table);
                Gap(body, 16);
                body.Children.Add(Text("Місячні кубки", 18, "Text", true));
                var cups = new WrapPanel();
                body.Children.Add(cups);
                foreach (var month in p.Cups)
                {
                    var cup = Card(Text("🏆 " + month, 18, "Accent", true), 8);
                    cup.Margin = new Thickness(3);
                    cups.Children.Add(cup);
                }
                if (p.Cups.Count == 0) body.Children.Add(Text("Кубків ще немає.", 13, "Muted"));
                Gap(body, 16);
                history.Children.Clear();
                var games = ProfileTable(1.6, .9, 1.2, .7);
                AddProfileRow(games, true, "Дата / режим", "Складність", "Результат / час", "Бали");
                foreach (var r in p.History)
                {
                    string outcome = r.Outcome switch { "completed" => "Перемога", "defeated" => "Поразка", _ => "Перервано" };
                    AddProfileRow(games, false, $"{r.Ended.ToLocalTime():g}\n{(r.ChallengeDay is null ? r.Source : "Щоденна " + r.ChallengeDay)}",
                        Level(r.Difficulty), outcome + " · " + FormatTime(r.Seconds), r.Score.ToString("N0"));
                }
                history.Children.Add(games);
                if (p.History.Count == 0) history.Children.Add(Text("Ігор ще немає.", 13, "Muted"));
                friendActions.Children.Clear();
                if (!own && account is not null && p.Verified && (await Request<AccountSettings>("account/settings")).Verified)
                {
                    var links = await Request<List<FriendEntry>>("friends");
                    if (!window.IsVisible || sessionToken != account?.Token) return;
                    AddFriendActions(friendActions, p.User, links.FirstOrDefault(x => x.User == p.User)?.Status, Load);
                }
                prev.IsEnabled = page > 0;
                next.IsEnabled = p.HasMore;
            }
            catch (Exception ex)
            {
                if (!window.IsVisible) return;
                body.Children.Clear();
                body.Children.Add(Text(ex.Message, 14, "Wrong"));
                prev.IsEnabled = page > 0;
            }
            finally
            {
                loading = false;
                if (reloadPending && window.IsVisible) { reloadPending = false; _ = Load(); }
            }
        }

        prev.Click += async (_, _) => { if (page > 0) { page--; await Load(); } };
        next.Click += async (_, _) => { page++; await Load(); };
        filter.SelectionChanged += async (_, _) => { page = 0; await Load(); };
        window.Loaded += async (_, _) => await Load();
        window.ShowDialog();
    }
}
