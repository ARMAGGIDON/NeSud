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
    private (Grid View, Func<string> Value, Action Clear) PasswordInput()
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var masked = new PasswordBox { Padding = new Thickness(8), MaxLength = 128,
            Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line") };
        var plain = new TextBox { Padding = new Thickness(8), MaxLength = 128, Visibility = Visibility.Collapsed,
            Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line") };
        bool shown = false;
        Button toggle = null!;
        toggle = Action("Показати", () =>
        {
            shown = !shown;
            if (shown)
            {
                plain.Text = masked.Password;
                masked.Clear();
            }
            else
            {
                masked.Password = plain.Text;
                plain.Clear();
            }
            masked.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            plain.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = shown ? "Приховати" : "Показати";
            if (shown) plain.Focus();
            else masked.Focus();
        });
        toggle.Margin = new Thickness(6, 0, 0, 0);
        toggle.MinWidth = 100;
        At(grid, masked, 0, 0);
        At(grid, plain, 0, 0);
        At(grid, toggle, 0, 1);
        return (grid, () => shown ? plain.Text : masked.Password, () => {
            masked.Clear();
            plain.Clear();
        });
    }

    private void ShowAccount()
    {
        if (state.PendingReward is not null && account is not null)
        {
            Dialog("Винагорода", "Спочатку дочекайтеся підтвердження отримання ресурсу.", false);
            return;
        }
        var panel = Stack();
        panel.Children.Add(Text("Акаунт", 24, "Text", true));
        Gap(panel);
        var window = Modal("Акаунт", panel, 480);
        if (account is not null)
        {
            panel.Children.Add(Text(account.Username ?? account.User, 18, "Text", true));
            
            panel.Children.Add(Text(reachable ? "● Онлайн • автоматична синхронізація" : "○ Офлайн • синхронізація після відновлення зв’язку", 13, "Muted"));
            Gap(panel, 18);
            panel.Children.Add(Action("Синхронізувати зараз", () => {
                window.Close();
                _ = Sync();
            }, primary: true));
            panel.Children.Add(Action("Вийти з акаунта", () =>
            {
                if (!Dialog("Вихід", "Перейти в гостьовий режим? Локальні ігри акаунта залишаться у його історії.", true)) return;
                var old = account;
                account = null;
                state.ProtectedSession = null;
                state.AccountScope = "";
                daily = null;
                SwitchProfileGame();
                Save();
                window.Close();
                if (old is not null) _ = Revoke(old, serverUrl);
            }));
        }
        else
        {
            var title = Text("Вхід до акаунта", 16, "Muted", true);
            panel.Children.Add(title);
            Gap(panel);
            var tabs = new UniformGrid { Columns = 2 };
            panel.Children.Add(tabs);
            bool register = false, working = false;
            var name = new TextBox { Padding = new Thickness(8), MaxLength = 254, Margin = new Thickness(0, 4, 0, 12),
                Background = B("Control"), Foreground = B("Text"), BorderBrush = B("Line"),
                Text = Scope.Length == 0 ? "" : Scope.Split('|')[^ 1] };
            var emailPanel = Stack();
            emailPanel.Children.Add(Text("Електронна пошта"));
            var email = new TextBox { MaxLength = 254 };
            emailPanel.Children.Add(email);
            emailPanel.Children.Add(Text("Код запрошення (необов’язково)"));
            var invite = new TextBox { MaxLength = 32 };
            emailPanel.Children.Add(invite);
            var password = PasswordInput();
            var confirm = PasswordInput();
            var confirmPanel = Stack();
            confirmPanel.Children.Add(Text("Підтвердження пароля"));
            confirmPanel.Children.Add(confirm.View);
            var notice = Text("", 13, "Wrong");
            var loginLabel = Text("Логін або email");
            Button submit = null!, loginTab = null!, registerTab = null!;
            void Mode(bool value)
            {
                register = value;
                loginLabel.Text = value ? "Нікнейм" : "Логін або email";
                emailPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                confirmPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                confirm.Clear();
                notice.Text = "";
                submit.Content = value ? "Зареєструватися" : "Увійти";
                title.Text = value ? "Створення акаунта" : "Вхід до акаунта";
                loginTab.Background = B(value ? "Control" : "Same");
                registerTab.Background = B(value ? "Same" : "Control");
            }
            loginTab = Action("Вхід", () => Mode(false));
            registerTab = Action("Реєстрація", () => Mode(true));
            tabs.Children.Add(loginTab);
            tabs.Children.Add(registerTab);
            Gap(panel);
            panel.Children.Add(loginLabel);
            panel.Children.Add(name);
            panel.Children.Add(emailPanel);
            panel.Children.Add(Text("Пароль (10–128 символів)"));
            panel.Children.Add(password.View);
            panel.Children.Add(confirmPanel);
            panel.Children.Add(notice);
            async Task Login()
            {
                if (working) return;
                string secret = password.Value(), confirmation = confirm.Value();
                if (state.PendingReward is not null && !string.Equals(Scope.Split('|')[^ 1], name.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    notice.Text = "Увійдіть до попереднього акаунта для відновлення винагороди.";
                    return;
                }
                if (name.Text.Trim().Length < 3)
                {
                    notice.Text = "Введіть логін або email.";
                    name.Focus();
                    return;
                }
                if (secret.Length is < 10 or > 128)
                {
                    notice.Text = "Пароль має містити від 10 до 128 символів.";
                    return;
                }
                if (register && secret != confirmation)
                {
                    notice.Text = "Паролі не збігаються.";
                    return;
                }
                if (register && string.IsNullOrWhiteSpace(email.Text))
                {
                    notice.Text = "Введіть електронну пошту.";
                    return;
                }
                working = true;
                submit.IsEnabled = loginTab.IsEnabled = registerTab.IsEnabled = false;
                notice.Text = "Підключення…";
                try
                {
                    var session = await Request<AccountSession > (register ? "register" : "login",
                        new Credentials(name.Text, secret, register ? confirmation : null, register ? email.Text : null, register ? invite.Text : null), token: "");
                    if (!window.IsVisible) return;
                    account = session;
                    reachable = true;
                    state.AccountScope = serverUrl + "|" + session.User;
                    RememberSession(session);
                    bool claim = (store.History("").Count > 0 || (string.IsNullOrEmpty(state.GameScope) && G is { Completed: false }))
                        && Dialog("Імпорт ігор гостя", "Прив’язати локальні ігри гостя до цього акаунта?", true);
                    if (claim)
                    {
                        store.ClaimGuest(Scope);
                        if (string.IsNullOrEmpty(state.GameScope) && G is not null) state.GameScope = Scope;
                    }
                    SwitchProfileGame();
                    password.Clear();
                    confirm.Clear();
                    Save();
                    UpdateConnection();
                    window.Close();
                    _ = Sync();
                    if (register) Dispatcher.BeginInvoke(new Action(() => _ = EditOwnProfile(true)));
                }
                catch (HttpRequestException ex) when (ex.StatusCode is not null)
                {
                    notice.Text = ex.Message;
                }
                catch
                {
                    notice.Text = "Не вдалося з’єднатися з сервісом. Спробуйте ще раз.";
                }
                finally
                {
                    working = false;
                    submit.IsEnabled = loginTab.IsEnabled = registerTab.IsEnabled = true;
                }
            }
            submit = Action("Увійти", () => _ = Login(), primary: true);
            submit.IsDefault = true;
            panel.Children.Add(submit);
            Mode(false);
            if (Scope.Length > 0 && state.PendingReward is null) panel.Children.Add(Action("Перейти в гостьовий режим", () =>
            {
                state.ProtectedSession = null;
                state.AccountScope = "";
                daily = null;
                SwitchProfileGame();
                Save();
                window.Close();
            }));
        }
        window.ShowDialog();
    }

    private void SwitchProfileGame()
    {
        if (state.PendingReward is not null) return;
        if (G is null || (state.GameScope ?? "") == Scope) return;
        ReadClock();
        var previous = Terminal(G.Completed ? "completed" : G.OutOfLives ? "defeated" : "abandoned");
        var oldScope = state.GameScope ?? "";
        clock.Reset();
        state.Game = null;
        state.GameId = Guid.Empty;
        state.GameScope = Scope;
        Save(previous, oldScope);
        Render();
    }

    private async Task Revoke(AccountSession session, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/api/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var response = await http.SendAsync(request);
        }
        catch { }
    }
}
