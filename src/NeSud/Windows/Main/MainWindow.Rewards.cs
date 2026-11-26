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
    private static string ResourceName(string kind) => kind switch
    { "life" => "Життя", "hint" => "Підказки", _ => "Кандидати" };
    private int WalletCount(string kind) => kind switch
    { "life" => daily?.Lives ?? 0, "hint" => daily?.Hints ?? 0, _ => daily?.Candidates ?? 0 };

    private async Task ChooseResource(string kind)
    {
        if (account is not null && reachable)
        {
            try { daily = await Request<DailyStatus>("daily"); }
            catch { }
            if (WalletCount(kind) > 0) { if (Dialog("Ресурс зі сховища", $"Використати 1 одиницю: {ResourceName(kind)}?", true)) await Spend(kind); return; }
        }
        Reward(kind);
    }

    private async Task Spend(string kind)
    {
        if (account is null || !reachable || state.PendingReward is not null || G?.CanReward(kind) != true) return;
        var pending = new PendingReward(Scope, new WalletRequest(Guid.NewGuid(), state.GameId, kind));
        state.PendingReward = pending;
        try { store.Save(state); }
        catch { state.PendingReward = null; status.Text = "Не вдалося зберегти операцію."; return; }
        SyncClock(); Render(); await ResumeSpend();
    }

    private async Task ResumeSpend()
    {
        if (spending || account is null || state.PendingReward is not { } pending ||
            pending.Scope != Scope || pending.Request.GameId != state.GameId || G is null) return;
        spending = true;
        var game = G; var session = account;
        try
        {
            var result = await Request<WalletResult>("daily/spend", pending.Request, token: session.Token);
            if (closing || !ReferenceEquals(session, account)) return;
            if (result.Operation != pending.Request.Operation || result.GameId != state.GameId || result.Kind != pending.Request.Kind)
                throw new InvalidDataException("Невірна відповідь.");
            var copy = Database.Decode<Game>(Database.Encode(game));
            if (!copy.GrantReward(result.Kind, advertisement: false)) throw new InvalidDataException("Ресурс не можна застосувати.");
            state.Game = copy; state.PendingReward = null;
            try { store.Save(state); }
            catch { state.Game = game; state.PendingReward = pending; throw; }
            daily = await Request<DailyStatus>("daily", token: session.Token);
            status.Text = "";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            state.PendingReward = null;
            try { store.Save(state); } catch { state.PendingReward = pending; }
            status.Text = "Ресурс недоступний.";
        }
        catch { status.Text = "Очікуємо підтвердження ресурсу. Операцію буде повторено."; }
        finally { spending = false; if (!closing) { SyncClock(); Render(); FocusCell(); } }
    }

    private async Task ShowDaily()
    {
        if (account is null) { ShowAccount(); return; }
        if (!weeklyRewards && !challengeRewards && G?.Completed != true) return;
        var panel = Stack(); panel.Children.Add(Text(weeklyRewards ? "Призи турніру" : challengeRewards ? "Нагороди щоденних ігор" : "Щоденні нагороди", 24, "Text", true));
        Gap(panel);
        var body = Stack(); panel.Children.Add(body);
        var window = Modal("Щоденні нагороди", panel, 540);
        var notice = Text("Завантаження…", 14, "Muted"); panel.Children.Add(notice);
        bool working = false;
        void Draw()
        {
            body.Children.Clear();
            if (daily is not { } d) return;
            body.Children.Add(Text((weeklyRewards || challengeRewards ? "" : $"Сьогодні: {Math.Min(7,d.Wins)}/7 перемог • ") + $"доступно наборів: {d.Packs.Count(p => weeklyRewards ? p.Milestone == "weekly" : challengeRewards ? p.Milestone == "challenge" : p.Milestone is not ("weekly" or "challenge"))}", 15, "Accent", true));
            body.Children.Add(Text($"Сховище: підказки {d.Hints} • кандидати {d.Candidates}", 14));
            Gap(body);
            foreach (var kind in new[] { "hint", "candidates" })
            {
                string k = kind;
                var use = Action($"Використати: {ResourceName(k)} ({WalletCount(k)})", () =>
                { window.Close(); _ = Spend(k); });
                use.IsEnabled = WalletCount(k) > 0 && G?.CanReward(k) == true && state.PendingReward is null;
                body.Children.Add(use);
            }
            var pack = d.Packs.FirstOrDefault(p => weeklyRewards ? p.Milestone == "weekly" : challengeRewards ? p.Milestone == "challenge" : p.Milestone is not ("weekly" or "challenge"));
            if (pack is null) { body.Children.Add(Text("Немає доступних нагород.", 13, "Muted")); return; }
            string milestone = pack.Milestone switch { "login" => "Щоденний вхід", "win1" => "Перша перемога",
                "win3" => "Третя перемога", "win7" => "Сьома перемога", "weekly" => "Тижневий турнір", "challenge" => $"Щоденна гра • {pack.Day}", "win" => $"Перемога {pack.Slot + 1}", _ => "Попередня нагорода" };
            Gap(body); body.Children.Add(Text(milestone, 17, "Text", true));
            var cards = new UniformGrid { Columns = 3 }; body.Children.Add(cards);
            for (int i = 0; i < 3; i++)
            {
                int card = i;
                var button = Action("?\nКартка " + (i + 1), () => _ = SelectCard(card), primary: true);
                button.Margin = new Thickness(4); button.MinHeight = 95; button.FontSize = 19; cards.Children.Add(button);
            }
            async Task SelectCard(int index)
            {
                if (working) return; working = true; cards.IsEnabled = false;
                try
                {
                    var result = await Request<DailyResult>("daily/choose", new DailyChoice(pack.Day, pack.Milestone, pack.Slot, index));
                    if (!window.IsVisible) return;
                    daily = result.Status;
                    for (int j = 0; j < cards.Children.Count; j++)
                        if (cards.Children[j] is Button cardButton) cardButton.Content = j == index ? $"{ResourceName(result.Item.Kind)}\n×{result.Item.Amount}" : "Закрито";
                    notice.Text = $"Отримано: {ResourceName(result.Item.Kind)} ×{result.Item.Amount}";
                    body.Children.Add(Action("Далі", () => Draw(), primary: true));
                    notice.Foreground = B("Accent");
                }
                catch { notice.Text = "Не вдалося отримати картку. Повторіть спробу."; cards.IsEnabled = true; }
                finally { working = false; }
            }
        }
        window.Loaded += async (_, _) =>
        {
            try { daily = await Request<DailyStatus>("daily"); if (window.IsVisible) { Draw(); notice.Text = ""; } }
            catch { notice.Text = "Щоденні нагороди доступні при підключенні до сервісу."; }
        };
        var close = Action("Закрити", () => window.Close()); close.IsCancel = true; panel.Children.Add(close);
        window.ShowDialog(); Render(); await Task.CompletedTask;
    }

    private void Reward(string kind)
    {
        if (Busy || watchingReward || state.PendingReward is not null || G is not { } game) return;
        bool revival = kind == "life";
        if (revival ? !game.BeginRevive(DateTimeOffset.UtcNow) : !game.CanAdReward(kind) || WalletCount(kind)>0) return;
        if (revival) { try { store.Save(state); } catch { game.CancelRevive(); Render(); return; } }
        watchingReward = true; SyncClock(); Render();
        var panel = Stack(); panel.Children.Add(Text("Демонстраційна реклама", 22, "Text", true));
        Gap(panel); panel.Children.Add(Text("Після завершення таймера можна отримати винагороду. Це заглушка, а не справжня реклама.", 14, "Muted"));
        Gap(panel); var countdown = Text("", 30, "Accent", true); panel.Children.Add(countdown);
        var bar = new ProgressBar { Minimum = 0, Maximum = Game.RewardSeconds, Height = 7, Margin = new Thickness(0, 8, 0, 18) };
        bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush"); panel.Children.Add(bar);
        var window = Modal("Винагорода", panel, 440);
        var elapsed = Stopwatch.StartNew();
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var claim = Action("Отримати винагороду", () =>
        {
            if (elapsed.Elapsed.TotalSeconds >= Game.RewardSeconds && game == G && (revival ? game.FinishRevive(DateTimeOffset.UtcNow) : game.GrantReward(kind)))
                window.DialogResult = true;
        }, primary: true);
        claim.IsEnabled = false; panel.Children.Add(claim);
        var cancel = Action("Скасувати", () => window.DialogResult = false); cancel.IsCancel = true; panel.Children.Add(cancel);
        void Update()
        {
            double passed = elapsed.Elapsed.TotalSeconds; bar.Value = Math.Min(Game.RewardSeconds, passed);
            countdown.Text = passed >= Game.RewardSeconds ? "Готово!" : $"{Math.Ceiling(Game.RewardSeconds - passed)} с";
            claim.IsEnabled = passed >= Game.RewardSeconds;
        }
        watch.Tick += (_, _) => Update(); window.Closed += (_, _) => watch.Stop();
        Update(); watch.Start();
        try { if (window.ShowDialog() == true) { status.Text = ""; Save(); } }
        finally { if (revival) game.CancelRevive(); watchingReward = false; SyncClock(); Render(); Save(); FocusCell(); }
    }
}
