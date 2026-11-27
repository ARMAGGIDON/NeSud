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
        var searchStatus = Text("Введіть щонайменше 2 символи.", 13, "Muted");
        panel.Children.Add(searchStatus);
        var results = Stack();
        var resultScroll = new ScrollViewer { Content = results, Height = 84, Margin = new Thickness(0, 5, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(resultScroll);
        var separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 16) };
        separator.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        panel.Children.Add(separator);
        var body = Stack();
        panel.Children.Add(body);
        var window = Page("Друзі", panel, 570);
        window.Reopen = () => _ = ShowFriends();
        int version = 0;
        var sessionToken = account?.Token;
        async Task Load()
        {
            int requestVersion = ++version;
            string query = search.Text.Trim();
            bool Current() => window.IsVisible && version == requestVersion && account?.Token == sessionToken;
            results.Children.Clear();
            searchStatus.Text = query.Length >= 2 ? "Пошук…" : "Введіть щонайменше 2 символи.";
            try
            {
                var friendsTask = Request<List<FriendEntry>>("friends");
                var matchesTask = query.Length >= 2 ? Request<List<string>>("players?q=" + Uri.EscapeDataString(query))
                    : Task.FromResult(new List<string>());
                try
                {
                    var friends = await friendsTask;
                    if (Current())
                    {
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
                }
                catch (Exception ex) { if (Current()) Dialog("Друзі", ex.Message, false); }
                var matches = await matchesTask;
                if (!Current() || search.Text.Trim() != query || query.Length < 2) return;
                var names = matches.Where(name => !string.Equals(name, account?.Username ?? account?.User,
                    StringComparison.OrdinalIgnoreCase)).ToList();
                searchStatus.Text = names.Count == 0 ? "Гравців не знайдено." : $"Знайдено: {names.Count}";
                foreach (var name in names)
                {
                    var player = PlayerButton(name);
                    player.HorizontalAlignment = HorizontalAlignment.Left;
                    results.Children.Add(player);
                }
            }
            catch (Exception ex) { if (Current() && search.Text.Trim() == query) searchStatus.Text = ex.Message; }
        }
        search.TextChanged += (_, _) =>
        {
            results.Children.Clear();
            searchStatus.Text = search.Text.Trim().Length >= 2 ? "Натисніть «Знайти / оновити»." : "Введіть щонайменше 2 символи.";
        };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = Load();
        };
        find.Click += (_, _) => _ = Load();
        window.Loaded += async (_, _) => await Load();
        window.ShowDialog();
    }
}
