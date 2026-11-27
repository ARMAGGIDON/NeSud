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
        if (user is not null && account is not null &&
            string.Equals(user, account.Username ?? account.User, StringComparison.OrdinalIgnoreCase))
            user = null;
        if (user is null && surfaces.LastOrDefault()?.Title == "Мій профіль") return;
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
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 340 };
        tools.VerticalAlignment = VerticalAlignment.Center;
        At(header, tools, 0, 2);
        var friendActions = Stack();
        friendActions.Margin = new Thickness(0, 8, 0, 0);
        identity.Children.Add(friendActions);
        var avatarHost = new Grid();
        At(header, avatarHost, 0, 0);
        var body = Stack();
        panel.Children.Add(body);
        var historyTitle = Text("Історія ігор", 18, "Text", true);
        panel.Children.Add(historyTitle);
        Gap(panel, 6);
        var filter = new ComboBox { ItemsSource = new[] { "Усі складності", "Легка", "Середня", "Складна", "Експерт" }, SelectedIndex = 0 };
        panel.Children.Add(Text("Складність", 12, "Muted"));
        panel.Children.Add(filter);
        var history = Stack();
        var historyScroll = new ScrollViewer
        {
            Content = history, Height = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(historyScroll);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(nav);
        var prev = Action("Попередня");
        var next = Action("Наступна");
        prev.Margin = new Thickness(0, 8, 4, 0);
        next.Margin = new Thickness(4, 8, 0, 0);
        nav.Children.Add(prev);
        nav.Children.Add(next);
        var window = Page(user is null ? "Мій профіль" : "Профіль", panel, 900);
        window.Reopen = () => _ = ShowProfile(user);
        int page = 0;
        bool loading = false;
        bool reloadPending = false, fullReloadPending = false, profileLoaded = false;
        bool own = user is null;
        string scope = Scope;
        string? sessionToken = account?.Token;

        Task Load() => LoadProfile(true);

        async Task LoadProfile(bool full)
        {
            full |= !profileLoaded;
            if (loading) { reloadPending = true; fullReloadPending |= full; return; }
            loading = true;
            int requestedPage = page, requestedLevel = filter.SelectedIndex;
            bool Current() => window.IsVisible && sessionToken == account?.Token && scope == Scope &&
                page == requestedPage && filter.SelectedIndex == requestedLevel;
            prev.IsEnabled = next.IsEnabled = false;
            try
            {
                bool local = own && account is null;
                if (full && own && account is not null && !reachable) _ = Probe();
                if (full && own && account is not null && reachable) await Sync();
                if (!Current()) return;
                PublicProfile p;
                OwnProfileDetails? details = null;
                try
                {
                    if (local) throw new InvalidOperationException("Локальний профіль");
                    p = await Request<PublicProfile>("profile?page=" + requestedPage +
                    (user is null ? "" : "&user=" + Uri.EscapeDataString(user)) +
                    (requestedLevel > 0 ? "&difficulty=" + (Difficulty)(requestedLevel - 1) : ""));
                    if (full && own) details = await Request<OwnProfileDetails>("profile/own");
                }
                catch when (own)
                {
                    local = true;
                    (p, details) = LocalProfile(scope, requestedPage, requestedLevel);
                }
                if (own && !local && store.History(scope, true).Count > 0)
                {
                    local = true;
                    (p, details) = LocalProfile(scope, requestedPage, requestedLevel);
                }
                if (!Current()) return;
                if (full)
                {
                    List<FriendEntry>? links = null;
                    if (!own && account is not null && p.Verified &&
                        !string.Equals(p.User, account.Username ?? account.User, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if ((await Request<AccountSettings>("account/settings")).Verified)
                                links = await Request<List<FriendEntry>>("friends");
                        }
                        catch { /* The profile remains available when friend actions cannot be loaded. */ }
                    }
                    if (!Current()) return;
                    // Commit the profile in one dispatcher turn, after all requests have completed.
                    if (own && !local && account is not null && account.Username != p.User)
                    {
                        account = account with { Username = p.User };
                        RememberSession(account);
                        UpdateConnection();
                    }
                    name.Text = p.User;
                    verified.Visibility = p.Verified ? Visibility.Visible : Visibility.Collapsed;
                    avatarHost.Children.Clear();
                    avatarHost.Children.Add(PlayerAvatar(p.User, 88, own && !local && p.Verified ? async () =>
                    {
                        try
                        {
                            await EditAvatar();
                            if (window.IsVisible) await Load();
                        }
                        catch (Exception ex) { if (window.IsVisible) Dialog("Аватар", ex.Message, false); }
                    } : null, refresh: true));
                    tools.Children.Clear();
                    if (own && account is not null && !local)
                    {
                        tools.Children.Add(Action(p.Verified ? "Налаштування профілю" : "Підтвердити пошту", async () =>
                        {
                            await EditOwnProfile();
                            if (sessionToken != account?.Token || scope != Scope) window.Close();
                            else if (window.IsVisible) await Load();
                        }));
                        if (p.Verified) tools.Children.Add(Action("Запрошення", async () => await ShowInvitations()));
                    }
                    if (own && account is null)
                        tools.Children.Add(Action("Увійти", () => { ShowAccount(); if (sessionToken != account?.Token || scope != Scope) window.Close(); }));
                    else if (own && local)
                        tools.Children.Add(Action("Налаштування профілю", async () =>
                        {
                            await EditOwnProfile();
                            if (sessionToken != account?.Token || scope != Scope) window.Close();
                            else if (window.IsVisible) await Load();
                        }));
                    foreach (Button button in tools.Children) button.Margin = new Thickness(4, 2, 0, 2);
                    body.Children.Clear();
                    body.Children.Add(Text("Статистика", 18, "Text", true));
                    if (local) body.Children.Add(Text("Локальні дані. Серверні результати можуть бути неповними.", 13, "Muted"));
                    var metrics = new List<(string, string)> {
                        ("Ігор", p.Games.ToString("N0")), ("Перемог", p.Wins.ToString("N0")),
                        ("Ідеальних", p.PerfectWins.ToString("N0")), ("Балів", p.Score.ToString("N0")),
                        ("Серія / рекорд", $"{p.CurrentStreak} / {p.BestStreak}"), ("Щоденних перемог", p.DailyWins.ToString("N0")) };
                    if (details is not null) metrics.AddRange([("Час", ProfileTime(details.Seconds)), ("Помилок", details.Mistakes.ToString("N0")), ("Підказок", details.Hints.ToString("N0"))]);
                    body.Children.Add(ProfileMetrics(metrics.ToArray()));
                    body.Children.Add(Text("За складностями", 18, "Text", true));
                    var table = ProfileTable(1.15, 1.1, .8, 1.1, 1, 1);
                    AddProfileRow(table, true, "Складність", "Перемоги / ігри", "Ідеальні", "Серія / рекорд", "Бали", "Найкращий час");
                    foreach (var s in p.ByDifficulty ?? [])
                        AddProfileRow(table, false, Level(s.Difficulty), $"{s.Wins} / {s.Games}", s.PerfectWins.ToString("N0"),
                            $"{s.CurrentStreak} / {s.BestStreak}", s.Score.ToString("N0"), s.BestSeconds is int best ? FormatTime(best) : "Немає");
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
                    if (own && details is not null) AddActiveGames(body, details.Active, window);
                    friendActions.Children.Clear();
                    if (links is not null)
                        AddFriendActions(friendActions, p.User, links.FirstOrDefault(x => x.User == p.User)?.Status, Load);
                    profileLoaded = true;
                }
                history.Children.Clear();
                var games = ProfileTable(1.6, .9, 1.2, .7);
                AddProfileRow(games, true, "Дата / режим", "Складність", "Результат / час", "Бали");
                foreach (var r in p.History)
                {
                    string outcome = r.Outcome switch { "completed" => "Перемога", "defeated" => "Поразка", _ => "Перервано" };
                    AddProfileRow(games, false, $"{r.Ended.ToLocalTime():g}\n{(r.ChallengeDay is null ? r.Source : "Щоденна " + r.ChallengeDay)}",
                        Level(r.Difficulty), outcome + "\n" + FormatTime(r.Seconds), r.Score.ToString("N0") + $"\nПомилки: {r.Mistakes}\nПідказки: {r.Hints}");
                }
                history.Children.Add(games);
                if (p.History.Count == 0) history.Children.Add(Text("Ігор ще немає.", 13, "Muted"));
                historyScroll.ScrollToTop();
                prev.IsEnabled = page > 0;
                next.IsEnabled = p.HasMore;
            }
            catch (Exception ex)
            {
                if (!Current()) return;
                if (body.Children.Count == 0) body.Children.Add(Text(ex.Message, 14, "Wrong"));
                else Dialog("Профіль", ex.Message, false);
                prev.IsEnabled = page > 0;
            }
            finally
            {
                loading = false;
                if (reloadPending && window.IsVisible)
                {
                    bool refreshAll = fullReloadPending || (full && !Current());
                    reloadPending = fullReloadPending = false;
                    _ = LoadProfile(refreshAll);
                }
            }
        }

        prev.Click += async (_, _) => { if (page > 0) { page--; await LoadProfile(false); } };
        next.Click += async (_, _) => { page++; await LoadProfile(false); };
        filter.SelectionChanged += async (_, _) => { page = 0; await LoadProfile(false); };
        window.Loaded += async (_, _) => await Load();
        window.ShowDialog();
        await Task.CompletedTask;
    }
}
