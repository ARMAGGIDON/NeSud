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
    private Func<bool>? dismissPlayerSearch;

    private void AddFriendActions(Panel panel, string user, string? status, Func<Task> reload)
    {
        var actions = new WrapPanel();
        if (panel is Grid) Grid.SetColumn(actions, 1);
        panel.Children.Add(actions);
        var choices = status switch { "incoming" => new[] { ("Прийняти", "accept"), ("Відхилити", "decline") },
            "outgoing" => new[] { ("Скасувати запит", "cancel") }, "friend" => new[] { ("Видалити друга", "remove") }, _ => new[] { ("Додати в друзі", "send") } };
        foreach (var (label, action) in choices)
        {
            var b = Action(label, async() => {
                try
                {
                    await Request<SyncAck > ("friends", new FriendRequest(user, action));
                    await reload();
                }
                catch (Exception ex)
                {
                    Dialog("Друзі", ex.Message, false);
                }
            });
            b.Margin = new Thickness(3);
            actions.Children.Add(b);
        }
    }
    private async Task ShowFriends()
    {
        if (account is null)
        {
            Dialog("Друзі", "Увійдіть та підтвердьте електронну пошту.", false);
            return;
        }
        try
        {
            if (!(await Request<AccountSettings>("account/settings")).Verified)
            {
                Dialog("Друзі", "Спочатку підтвердьте електронну пошту в профілі.", false);
                return;
            }
        }
        catch (Exception ex)
        {
            Dialog("Друзі", ex.Message, false);
            return;
        }

        var panel = Stack();
        panel.Children.Add(Text("Друзі та пошук", 24, "Text", true));
        var form = new Grid { Margin = new Thickness(0, 8, 0, 10), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = new TextBox { MaxLength = 80, Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(search, "Нікнейм гравця");
        search.ToolTip = "Нікнейм гравця";
        At(form, search, 0, 0);
        var find = Action("Знайти / оновити");
        find.Margin = new Thickness(0);
        At(form, find, 0, 1);
        panel.Children.Add(form);
        var searchStatus = Text("", 13, "Muted");
        var results = Stack();
        var resultScroll = new ScrollViewer { Content = results, MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var popupBody = Stack();
        popupBody.Children.Add(searchStatus);
        Gap(popupBody, 5);
        popupBody.Children.Add(resultScroll);
        var popupCard = Card(popupBody, 9);
        popupCard.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        var popup = new Popup { PlacementTarget = search, Placement = PlacementMode.Bottom, VerticalOffset = 5,
            AllowsTransparency = true, StaysOpen = true, Child = popupCard,
            PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None };
        Gap(panel, 8);
        var body = Stack();
        panel.Children.Add(body);
        var window = Page("Друзі", panel, 570);
        window.Reopen = () => _ = ShowFriends();
        int version = 0, searchVersion = 0;
        var sessionToken = account?.Token;
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        bool CurrentPage() => window.IsVisible && surfaces.LastOrDefault() == window && account?.Token == sessionToken;
        void HideResults()
        {
            ++searchVersion;
            debounce.Stop();
            popup.IsOpen = false;
        }
        async Task Search()
        {
            debounce.Stop();
            int requestVersion = ++searchVersion;
            string query = search.Text.Trim();
            bool Current() => CurrentPage() && requestVersion == searchVersion && search.Text.Trim() == query;
            if (!CurrentPage() || query.Length < 2) { HideResults(); return; }
            results.Children.Clear();
            searchStatus.Text = "Пошук…";
            popupCard.Width = Math.Max(200, search.ActualWidth);
            popup.IsOpen = true;
            try
            {
                var matches = await Request<List<string>>("players?q=" + Uri.EscapeDataString(query));
                if (!Current()) return;
                var names = matches.Where(name => !string.Equals(name, account?.Username ?? account?.User,
                    StringComparison.OrdinalIgnoreCase)).ToList();
                searchStatus.Text = names.Count == 0 ? "Гравців не знайдено." : $"Знайдено: {names.Count}";
                foreach (var name in names)
                {
                    var user = name;
                    var player = Action(user, () => { HideResults(); _ = ShowProfile(user); });
                    player.Content = PlayerIdentity(user, 30);
                    player.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    player.Background = Brushes.Transparent;
                    player.BorderBrush = Brushes.Transparent;
                    player.Margin = new Thickness(0, 2, 0, 0);
                    results.Children.Add(player);
                }
            }
            catch (Exception ex) { if (Current()) searchStatus.Text = ex.Message; }
        }
        async Task Load()
        {
            int requestVersion = ++version;
            bool Current() => CurrentPage() && version == requestVersion;
            try
            {
                var friends = await Request<List<FriendEntry>>("friends");
                if (!Current()) return;
                body.Children.Clear();
                foreach (var (status, label) in new[] { ("incoming", "Вхідні запити"), ("outgoing", "Надіслані запити"), ("friend", "Друзі") })
                {
                    body.Children.Add(Text(label, 17, "Text", true));
                    var entries = friends.Where(x => x.Status == status).ToList();
                    if (entries.Count == 0)
                        body.Children.Add(Text(status == "friend" ? "Друзів ще немає." : "Запитів немає.", 13, "Muted"));
                    foreach (var f in entries)
                    {
                        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                        row.ColumnDefinitions.Add(new ColumnDefinition());
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        At(row, PlayerButton(f.User), 0, 0);
                        AddFriendActions(row, f.User, f.Status, Load);
                        body.Children.Add(row);
                    }
                    Gap(body, 14);
                }
            }
            catch (Exception ex) { if (Current()) Dialog("Друзі", ex.Message, false); }
        }
        void NavigateResults(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && popup.IsOpen)
            {
                HideResults();
                search.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key is not (Key.Down or Key.Up)) return;
            if (!popup.IsOpen)
            {
                if (e.Key == Key.Down && search.Text.Trim().Length >= 2) { _ = Search(); e.Handled = true; }
                return;
            }
            var rows = results.Children.OfType<Button>().ToList();
            if (rows.Count == 0) return;
            int index = Keyboard.FocusedElement is Button focused ? rows.IndexOf(focused) : -1;
            if (e.Key == Key.Up && index <= 0) search.Focus();
            else rows[Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, rows.Count - 1)].Focus();
            e.Handled = true;
        }
        debounce.Tick += async (_, _) => await Search();
        search.TextChanged += (_, _) =>
        {
            HideResults();
            if (search.Text.Trim().Length >= 2) debounce.Start();
        };
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = Search(); return; }
            NavigateResults(e);
        };
        popupCard.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) { HideResults(); search.Focus(); e.Handled = true; return; }
            NavigateResults(e);
        };
        void OutsideClick(object sender, MouseButtonEventArgs e)
        {
            if (!form.IsMouseOver) HideResults();
        }
        void DeactivateSearch(object? sender, EventArgs e) => HideResults();
        bool EscapeSearch()
        {
            if (!popup.IsOpen) return false;
            HideResults();
            search.Focus();
            return true;
        }
        find.Click += (_, _) => { _ = Search(); _ = Load(); };
        search.IsEnabledChanged += (_, _) => { if (!search.IsEnabled) HideResults(); };
        window.Loaded += async (_, _) =>
        {
            PreviewMouseDown += OutsideClick;
            Deactivated += DeactivateSearch;
            LocationChanged += DeactivateSearch;
            dismissPlayerSearch = EscapeSearch;
            await Load();
        };
        window.Closed += (_, _) =>
        {
            HideResults();
            PreviewMouseDown -= OutsideClick;
            Deactivated -= DeactivateSearch;
            LocationChanged -= DeactivateSearch;
            if (dismissPlayerSearch == EscapeSearch) dismissPlayerSearch = null;
        };
        window.ShowDialog();
    }
}
