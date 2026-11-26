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
    private void AddFriendActions(StackPanel panel, string user, string? status, Func<Task> reload)
    {
        var actions = new WrapPanel();
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
        var panel = Stack();
        panel.Children.Add(Text("Друзі та пошук гравців", 24, "Text", true));
        var search = new TextBox { MinWidth = 250, Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 6) };
        panel.Children.Add(Text("Нікнейм гравця", 13, "Muted"));
        panel.Children.Add(search);
        var body = Stack();
        panel.Children.Add(new ScrollViewer { Content = body, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var window = Modal("Друзі", panel, 570);
        OwnModal(window);
        async Task Load()
        {
            body.Children.Clear();
            try
            {
                if (account is not null)
                {
                    var friends = await Request<List<FriendEntry>>("friends");
                    foreach (var status in new[] { "incoming", "outgoing", "friend" })
                    {
                        body.Children.Add(Text(status == "incoming"?"Вхідні запити": status == "outgoing"?"Надіслані запити": "Друзі", 17, "Text", true));
                        foreach (var f in friends.Where(x => x.Status == status))
                        {
                            body.Children.Add(Action(f.User, () => _ = ShowProfile(f.User)));
                            AddFriendActions(body, f.User, f.Status, Load);
                        }
                    }
                }
                if (account is null) body.Children.Add(Text("Увійдіть, щоб надсилати запити в друзі.", 13, "Muted"));
                if (search.Text.Trim().Length >= 2)
                {
                    body.Children.Add(Text("Результати пошуку", 17, "Text", true));
                    var matches = await Request<List<string>>("players?q=" + Uri.EscapeDataString(search.Text.Trim()), token: "");
                    foreach (var name in matches) body.Children.Add(Action(name, () => _ = ShowProfile(name)));
                    if (matches.Count == 0) body.Children.Add(Text("Гравців не знайдено.", 13, "Muted"));
                }
            }
            catch (Exception ex)
            {
                body.Children.Add(Text(ex.Message, 14, "Wrong"));
            }
        }
        var find = Action("Знайти / оновити", () => _ = Load());
        find.IsDefault = true;
        panel.Children.Insert(3, find);
        panel.Children.Add(Action("Закрити", () => window.Close()));
        window.Loaded += async (_, _) => await Load();
        window.ShowDialog();
        await Task.CompletedTask;
    }
}
