using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, (BitmapSource? Image, DateTimeOffset Checked, DateTimeOffset Expires)> avatarCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<BitmapSource?>> avatarRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim avatarSlots = new(4);
    private readonly Dictionary<string, System.Net.Http.Headers.EntityTagHeaderValue> avatarTags = new(StringComparer.OrdinalIgnoreCase);
    private string? headerAvatarUser;

    private void InvalidatePlayerAvatar(string user)
    {
        avatarCache.Remove(user);
        avatarTags.Remove(user);
    }

    private async Task<BitmapSource?> FetchPlayerAvatar(string user)
    {
        await avatarSlots.WaitAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, serverUrl.TrimEnd('/') + "/api/avatar?user=" + Uri.EscapeDataString(user));
            if (avatarCache.ContainsKey(user) && avatarTags.TryGetValue(user, out var tag))
                request.Headers.IfNoneMatch.Add(tag);
            using var response = await http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && avatarCache.TryGetValue(user, out var previous))
                return previous.Image;
            if (!response.IsSuccessStatusCode)
                return response.StatusCode != System.Net.HttpStatusCode.NotFound && avatarCache.TryGetValue(user, out var retained)
                    ? retained.Image : null;
            if (response.Content.Headers.ContentLength > 5 * 1024 * 1024) return null;
            var image = ReadAvatar(await response.Content.ReadAsByteArrayAsync());
            if (response.Headers.ETag is { } current) avatarTags[user] = current;
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or NotSupportedException or FormatException)
        { return avatarCache.TryGetValue(user, out var retained) ? retained.Image : null; }
        finally { avatarSlots.Release(); }
    }

    private async Task<BitmapSource?> PlayerAvatarImage(string user, bool fresh = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (avatarCache.TryGetValue(user, out var cached) &&
            (fresh ? now - cached.Checked < TimeSpan.FromSeconds(5) : cached.Expires > now))
            return cached.Image;
        if (avatarRequests.TryGetValue(user, out var pending)) return await pending;
        var request = FetchPlayerAvatar(user);
        avatarRequests[user] = request;
        try
        {
            var image = await request;
            if (avatarCache.Count >= 256) { avatarCache.Clear(); avatarTags.Clear(); }
            var checkedAt = DateTimeOffset.UtcNow;
            avatarCache[user] = (image, checkedAt, checkedAt.AddSeconds(image is null ? 5 : 60));
            return image;
        }
        finally { avatarRequests.Remove(user); }
    }

    private FrameworkElement PlayerAvatar(string user, double size, Func<Task>? edit = null, bool refresh = false)
    {
        var grid = new Grid { Width = size, Height = size, ClipToBounds = true };
        var background = new Border { CornerRadius = new CornerRadius(size / 6), BorderThickness = new Thickness(1) };
        background.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        background.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        grid.Children.Add(background);
        var initial = Text(string.IsNullOrEmpty(user) ? "•" : user[..1].ToUpperInvariant(), size * .4, "Muted", true);
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(initial);
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(1), Visibility = Visibility.Collapsed };
        grid.Children.Add(image);
        grid.SizeChanged += (_, _) => grid.Clip = new RectangleGeometry(new Rect(0, 0, size, size), size / 6, size / 6);
        bool loading = false;
        async Task Load(bool fresh)
        {
            if (loading) return;
            loading = true;
            try
            {
                var source = await PlayerAvatarImage(user, fresh);
                if (!grid.IsLoaded || source is null) return;
                image.Source = source;
                image.Visibility = Visibility.Visible;
                initial.Visibility = Visibility.Collapsed;
            }
            catch { }
            finally { loading = false; }
        }
        var timer = refresh ? new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) } : null;
        if (timer is not null) timer.Tick += async (_, _) => { if (grid.IsVisible) await Load(true); };
        grid.Loaded += async (_, _) =>
        {
            timer?.Start();
            await Load(refresh);
        };
        grid.Unloaded += (_, _) => timer?.Stop();
        if (edit is not null)
        {
            var button = Action("✎", tip: "Змінити аватар");
            button.FontSize = size * .35;
            button.Margin = new Thickness(0);
            button.Padding = new Thickness(0);
            button.Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            button.Foreground = Brushes.White;
            button.Opacity = 0;
            System.Windows.Automation.AutomationProperties.SetName(button, "Змінити аватар");
            grid.Children.Add(button);
            void Fade() => button.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(grid.IsMouseOver || button.IsKeyboardFocused ? 1 : 0, TimeSpan.FromMilliseconds(160)));
            grid.MouseEnter += (_, _) => Fade();
            grid.MouseLeave += (_, _) => Fade();
            button.IsKeyboardFocusedChanged += (_, _) => Fade();
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try { await edit(); }
                finally { button.IsEnabled = true; }
            };
        }
        return grid;
    }

    private FrameworkElement PlayerIdentity(string user, double size, string? label = null, bool refresh = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        At(grid, PlayerAvatar(user, size, refresh: refresh), 0, 0);
        var name = Text(label ?? user, 14, "Text", true);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(8, 0, 0, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        At(grid, name, 0, 1);
        return grid;
    }

    private Button PlayerButton(string user, string? label = null)
    {
        var button = Action(user, () => _ = ShowProfile(user));
        button.Content = PlayerIdentity(user, 32, label);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(8, 5, 8, 5);
        return button;
    }

    private void RefreshAccountAvatar(bool force = false)
    {
        if (accountNavigation is null) return;
        string? user = account is null ? null : account.Username ?? account.User;
        if (!force && headerAvatarUser == user) return;
        headerAvatarUser = user;
        accountNavigation.Content = user is null ? "Увійти" : PlayerIdentity(user, 26, "Мій профіль", refresh: true);
    }

    private FrameworkElement VerifiedMark()
    {
        var mark = Text("✓", 14, "Accent", true);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var frame = new Border { Child = mark, Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
            Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Пошта підтверджена" };
        frame.SetResourceReference(Border.BackgroundProperty, "SameBrush");
        System.Windows.Automation.AutomationProperties.SetName(frame, "Пошта підтверджена");
        return frame;
    }

    private FrameworkElement ProfileMetrics(params (string Label, string Value)[] values)
    {
        var grid = new UniformGrid { Columns = Math.Min(3, values.Length), Margin = new Thickness(0, 6, 0, 16) };
        foreach (var (label, value) in values)
        {
            var content = Stack();
            content.Children.Add(Text(label, 12, "Muted"));
            Gap(content, 5);
            content.Children.Add(Text(value, 23, "Accent", true));
            var card = Card(content, 12);
            card.Margin = new Thickness(3);
            grid.Children.Add(card);
        }
        return grid;
    }

    private static Grid ProfileTable(params double[] weights)
    {
        var table = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        foreach (double weight in weights)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
        return table;
    }

    private void AddProfileRow(Grid table, bool heading, params string[] values)
    {
        int row = table.RowDefinitions.Count;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var background = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        background.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        if (heading) background.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        At(table, background, row, 0, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            var label = Text(values[i], 13, heading ? "Muted" : "Text", heading);
            label.Margin = new Thickness(8, 9, 8, 9);
            label.VerticalAlignment = VerticalAlignment.Center;
            At(table, label, row, i);
        }
    }

    private Grid PlayerListGrid(int metrics)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        for (int i = 0; i < metrics; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }

    private void AddPlayerListHeading(StackPanel parent, params string[] labels)
    {
        var grid = PlayerListGrid(labels.Length);
        At(grid, Text("№", 12, "Muted", true), 0, 0);
        At(grid, Text("Гравець", 12, "Muted", true), 0, 1);
        for (int i = 0; i < labels.Length; i++)
            At(grid, Text(labels[i], 12, "Muted", true), 0, i + 2);
        parent.Children.Add(grid);
    }

    private void AddPlayerRow(StackPanel parent, string user, int rank, params string[] values)
    {
        var grid = PlayerListGrid(values.Length);
        var number = Text(rank.ToString(), 14, "Muted", true);
        number.VerticalAlignment = VerticalAlignment.Center;
        At(grid, number, 0, 0);
        At(grid, PlayerButton(user), 0, 1);
        for (int i = 0; i < values.Length; i++)
        {
            var metric = Text(values[i], 14);
            metric.Margin = new Thickness(8, 0, 0, 0);
            metric.VerticalAlignment = VerticalAlignment.Center;
            At(grid, metric, 0, i + 2);
        }
        parent.Children.Add(grid);
    }

    private async Task ShowInvitations()
    {
        var sessionToken = account?.Token;
        if (sessionToken is null) return;
        var parent = surfaces.LastOrDefault();
        try
        {
            var settings = await Request<AccountSettings>("account/settings");
            if (!settings.Verified || settings.Invite is null || account?.Token != sessionToken ||
                closing || parent != surfaces.LastOrDefault()) return;
            var panel = Stack();
            panel.Children.Add(Text("Запрошення", 24, "Text", true));
            panel.Children.Add(ProfileMetrics(("Підтверджених запрошень", settings.Referrals.ToString("N0"))));
            panel.Children.Add(Text("Персональне посилання", 13, "Muted"));
            var link = serverUrl.TrimEnd('/') + "/?invite=" + Uri.EscapeDataString(settings.Invite);
            var field = new Grid { Margin = new Thickness(0, 6, 0, 12) };
            var input = new TextBox { Text = link, IsReadOnly = true, Padding = new Thickness(9, 6, 48, 6) };
            field.Children.Add(input);
            var copy = Action("⧉", tip: "Копіювати посилання");
            copy.HorizontalAlignment = HorizontalAlignment.Right;
            copy.VerticalAlignment = VerticalAlignment.Stretch;
            copy.Width = 38;
            copy.MinHeight = 0;
            copy.Padding = new Thickness(0);
            copy.Margin = new Thickness(3);
            copy.FontSize = 19;
            var scale = new ScaleTransform(1, 1);
            copy.RenderTransformOrigin = new Point(.5, .5);
            copy.RenderTransform = scale;
            System.Windows.Automation.AutomationProperties.SetName(copy, "Копіювати посилання");
            int generation = 0;
            copy.Click += async (_, _) =>
            {
                try
                {
                    Clipboard.SetText(link);
                    int current = ++generation;
                    copy.Content = "✓";
                    copy.ToolTip = "Скопійовано";
                    var animation = new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(200));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
                    await Task.Delay(1400);
                    if (generation == current) { copy.Content = "⧉"; copy.ToolTip = "Копіювати посилання"; }
                }
                catch { input.Focus(); input.SelectAll(); }
            };
            field.Children.Add(copy);
            panel.Children.Add(field);
            panel.Children.Add(Text("За кожного нового підтвердженого гравця: 3 підказки та 3 кандидати.", 13, "Muted"));
            Modal("Запрошення", panel, 580).ShowDialog();
        }
        catch (Exception ex) { Dialog("Запрошення", ex.Message, false); }
    }
}
