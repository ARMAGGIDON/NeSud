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
            try
            {
                daily = await Request<DailyStatus > ("daily");
            }
            catch { }
            if (WalletCount(kind) > 0)
            {
                if (Dialog("Ресурс зі сховища", $"Використати 1 одиницю: {ResourceName(kind)}?", true)) await Spend(kind);
                return;
            }
        }
        Reward(kind);
    }

    private async Task Spend(string kind)
    {
        if (account is null || !reachable || state.PendingReward is not null || G?.CanReward(kind) != true) return;
        var pending = new PendingReward(Scope, new WalletRequest(Guid.NewGuid(), state.GameId, kind));
        state.PendingReward = pending;
        try
        {
            store.Save(state);
        }
        catch
        {
            state.PendingReward = null;
            status.Text = "Не вдалося зберегти операцію.";
            return;
        }
        SyncClock();
        Render();
        await ResumeSpend();
    }

    private async Task ResumeSpend()
    {
        if (spending || account is null || state.PendingReward is not { } pending ||
            pending.Scope != Scope || pending.Request.GameId != state.GameId || G is null) return;
        spending = true;
        var game = G;
        var session = account;
        try
        {
            var result = await Request<WalletResult > ("daily/spend", pending.Request, token: session.Token);
            if (closing || !ReferenceEquals(session, account)) return;
            if (result.Operation != pending.Request.Operation || result.GameId != state.GameId || result.Kind != pending.Request.Kind)
                throw new InvalidDataException("Невірна відповідь.");
            var copy = Database.Decode<Game > (Database.Encode(game));
            if (!copy.GrantReward(result.Kind, advertisement: false)) throw new InvalidDataException("Ресурс не можна застосувати.");
            state.Game = copy;
            state.PendingReward = null;
            try
            {
                store.Save(state);
            }
            catch
            {
                state.Game = game;
                state.PendingReward = pending;
                throw;
            }
            daily = await Request<DailyStatus > ("daily", token: session.Token);
            status.Text = "";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            state.PendingReward = null;
            try
            {
                store.Save(state);
            }
            catch
            {
                state.PendingReward = pending;
            }
            status.Text = "Ресурс недоступний.";
        }
        catch
        {
            status.Text = "Очікуємо підтвердження ресурсу. Операцію буде повторено.";
        }
        finally
        {
            spending = false;
            if (!closing)
            {
                SyncClock();
                Render();
                FocusCell();
            }
        }
    }

    private Guid pendingWinReward;
    private DailyStatus? pendingWinBaseline;
    private bool giftDialogOpen;
    private async Task ShowWinReward(Guid id, string? day = null)
    {
        if (account is null || closing || giftDialogOpen) return;
        if (day is null && (state.GameId != id || G?.Completed != true || surfaces.Count != 0)) return;
        var session = account;
        try
        {
            if (day is not null) daily = await Request<DailyStatus>("daily", token: session.Token);
            if (closing || !ReferenceEquals(account, session) || giftDialogOpen) return;
            if (day is null && (state.GameId != id || G?.Completed != true || surfaces.Count != 0)) return;
            if (daily is not { } updated) return;
            if (day is not null)
            {
                if (updated.Packs.Any(p => p.Milestone == "challenge" && p.Day == day))
                    await ShowDaily(day, automatic: true);
                return;
            }
            var baseline = pendingWinBaseline;
            pendingWinReward = Guid.Empty;
            pendingWinBaseline = null;
            int to = Math.Clamp(updated.Wins, 0, 7);
            int from = baseline is null ? Math.Max(0, to - 1) :
                baseline.Day == updated.Day ? Math.Clamp(baseline.Wins, 0, 7) : 0;
            if ((baseline is null && updated.Wins > 7) || to <= from) return;
            await ShowDaily(automatic: true, progressFrom: from, progressTo: to);
        }
        catch { }
    }

    private async Task ShowDaily(string? onlyDay = null, bool automatic = false, int progressFrom = -1, int progressTo = -1)
    {
        if (account is null)
        {
            ShowAccount();
            return;
        }
        if (giftDialogOpen) return;
        bool weekly = !automatic && weeklyRewards, challenge = automatic ? onlyDay is not null : challengeRewards;
        if (!weekly && !challenge && G?.Completed != true) return;
        string? questDay = daily?.Day;
        bool Matches(DailyPack p)
        {
            if (weekly) return p.Milestone == "weekly";
            if (challenge) return p.Milestone == "challenge" && (onlyDay is null || p.Day == onlyDay);
            if (!automatic) return p.Milestone is not ("weekly" or "challenge");
            int milestone = p.Milestone switch { "win1" => 1, "win3" => 3, "win7" => 7, _ => 0 };
            return p.Day == questDay && milestone > progressFrom && milestone <= progressTo;
        }
        var session = account;
        var panel = Stack();
        var body = Stack();
        panel.Children.Add(body);
        var window = GiftOverlay(new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        var notice = Text("", 16, "Accent", true);
        notice.MaxWidth = 360;
        notice.TextAlignment = TextAlignment.Center;
        panel.Children.Add(notice);
        bool working = false;
        void Draw()
        {
            body.Children.Clear();
            if (daily is not { } d) return;
            var pack = d.Packs.FirstOrDefault(Matches);
            if (pack is null)
            {
                var empty = Text("Немає доступних нагород.", 18, "Text", true);
                empty.TextAlignment = TextAlignment.Center;
                body.Children.Add(empty);
                body.Children.Add(Action("Готово", () => window.Close()));
                return;
            }
            var triangle = new Canvas { Width = 320, Height = 320 };
            body.Children.Add(triangle);
            const double distance = 168, size = 132;
            double height = distance * Math.Sqrt(3) / 2;
            var gifts = new GiftView[3];
            int? attempted = null;
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var gift = gifts[i] = MakeGift(index);
                Canvas.SetLeft(gift.Button, 160 - size / 2 + (i == 0 ? 0 : i == 1 ? -distance / 2 : distance / 2));
                Canvas.SetTop(gift.Button, 28 + (i == 0 ? 0 : height));
                triangle.Children.Add(gift.Button);
                gift.Button.Click += async (_, _) => await SelectGift(index);
            }
            async Task SelectGift(int index)
            {
                if (working || (attempted is int prior && prior != index)) return;
                attempted = index;
                working = true;
                foreach (var g in gifts) g.Button.IsEnabled = false;
                try
                {
                    var result = await Request<DailyResult>("daily/choose", new DailyChoice(pack.Day, pack.Milestone, pack.Slot, index), token: session.Token);
                    if (!ReferenceEquals(account, session)) return;
                    daily = result.Status;
                    if (!window.IsVisible) return;
                    notice.Text = $"Отримано: {ResourceName(result.Item.Kind)} ×{result.Item.Amount}";
                    notice.Foreground = B("Accent");
                    await RevealGift(gifts[index], result.Item);
                    if (!window.IsVisible) return;
                    foreach (var gift in gifts.Where((_, i) => i != index)) gift.Button.Opacity = .35;
                    body.Children.Add(Action(daily.Packs.Any(Matches) ? "Далі" : "Готово", () =>
                    {
                        if (!daily.Packs.Any(Matches)) window.Close();
                        else { notice.Text = ""; Draw(); }
                    }, primary: true));
                }
                catch
                {
                    notice.Text = "Не вдалося підтвердити нагороду. Повторіть вибір того самого подарунка.";
                    gifts[index].Button.IsEnabled = true;
                }
                finally { working = false; }
            }
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                var updated = automatic && daily is not null ? daily : await Request<DailyStatus>("daily", token: session.Token);
                if (!ReferenceEquals(account, session)) { window.Close(); return; }
                daily = updated;
                if (window.IsVisible)
                {
                    notice.Text = "";
                    if (progressFrom >= 0)
                    {
                        working = true;
                        try { await AnimateQuest(body, progressFrom, progressTo); }
                        finally { working = false; }
                        if (!window.IsVisible) return;
                        if (!daily.Packs.Any(Matches)) { window.Close(); return; }
                    }
                    Draw();
                    Fade(body);
                }
            }
            catch
            {
                notice.Text = "Щоденні нагороди доступні при підключенні до сервісу.";
            }
        };
        window.Closing += (_, e) => { if (working) e.Cancel = true; };
        giftDialogOpen = true;
        try { window.ShowDialog(); }
        finally { giftDialogOpen = false; }
        Render();
        await Task.CompletedTask;
    }

    private void Reward(string kind)
    {
        if (Busy || watchingReward || state.PendingReward is not null || G is not { } game) return;
        bool revival = kind == "life";
        if (revival ? !game.BeginRevive(DateTimeOffset.UtcNow) : !game.CanAdReward(kind) || WalletCount(kind) > 0) return;
        if (revival)
        {
            try
            {
                store.Save(state);
            }
            catch
            {
                game.CancelRevive();
                Render();
                return;
            }
        }
        watchingReward = true;
        SyncClock();
        Render();
        var panel = Stack();
        panel.Children.Add(Text("Демонстраційна реклама", 22, "Text", true));
        Gap(panel);
        panel.Children.Add(Text("Після завершення таймера можна отримати винагороду. Це заглушка, а не справжня реклама.", 14, "Muted"));
        Gap(panel);
        var countdown = Text("", 30, "Accent", true);
        panel.Children.Add(countdown);
        var bar = new ProgressBar { Minimum = 0, Maximum = Game.RewardSeconds, Height = 7, Margin = new Thickness(0, 8, 0, 18) };
        bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        panel.Children.Add(bar);
        var window = Modal("Винагорода", panel, 440);
        var elapsed = Stopwatch.StartNew();
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var claim = Action("Отримати винагороду", () =>
        {
            if (elapsed.Elapsed.TotalSeconds >= Game.RewardSeconds && game == G && (revival ? game.FinishRevive(DateTimeOffset.UtcNow) : game.GrantReward(kind)))
                window.DialogResult = true;
        }, primary: true);
        claim.IsEnabled = false;
        panel.Children.Add(claim);
        var cancel = Action("Скасувати", () => window.DialogResult = false);
        panel.Children.Add(cancel);
        void Update()
        {
            double passed = elapsed.Elapsed.TotalSeconds;
            bar.Value = Math.Min(Game.RewardSeconds, passed);
            countdown.Text = passed >= Game.RewardSeconds ? "Готово!" : $"{Math.Ceiling(Game.RewardSeconds - passed)} с";
            claim.IsEnabled = passed >= Game.RewardSeconds;
        }
        watch.Tick += (_, _) => Update();
        window.Closed += (_, _) => watch.Stop();
        Update();
        watch.Start();
        try
        {
            if (window.ShowDialog() == true)
            {
                status.Text = "";
                Save();
            }
        }
        finally
        {
            if (revival) game.CancelRevive();
            watchingReward = false;
            SyncClock();
            Render();
            Save();
            FocusCell();
        }
    }
}
