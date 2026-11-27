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
    private async Task ShowChallenges()
    {
        if (account is null)
        {
            ShowAccount();
            return;
        }
        string? month = null;
        int navigation = homeNavigationVersion;
        while (account is not null && !closing && navigation == homeNavigationVersion)
        {
            string? selectedDay = null;
            bool claim = false;
            var panel = Stack();
            var title = Text("Щоденні ігри", 24, "Text", true);
            panel.Children.Add(title);
            var nav = new UniformGrid { Columns = 3 };
            panel.Children.Add(nav);
            var previous = Action("‹");
            var progress = Text("", 16, "Accent", true);
            progress.TextAlignment = TextAlignment.Center;
            var next = Action("›");
            nav.Children.Add(previous);
            nav.Children.Add(progress);
            nav.Children.Add(next);
            var grid = new UniformGrid { Columns = 7 };
            panel.Children.Add(grid);
            var cups = new WrapPanel();
            panel.Children.Add(Text("Місячні кубки", 17, "Text", true));
            panel.Children.Add(cups);
            var notice = Text("", 13, "Muted");
            panel.Children.Add(notice);
            var window = Page("Щоденні ігри", panel, 560, 760);
            var reward = Action("Отримати нагороду", () => {
                claim = true;
                window.Close();
            }, primary: true);
            reward.Visibility = Visibility.Collapsed;
            panel.Children.Add(reward);
            bool loading = false;
            previous.Click += (_, _) => MoveMonth(- 1);
            next.Click += (_, _) => MoveMonth(1);
            void MoveMonth(int delta)
            {
                if (loading || month is null)return;
                var d = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(delta);
                month = d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                _ = Load();
            }
            async Task Load()
            {
                if (loading)return;
                loading = true;
                previous.IsEnabled = next.IsEnabled = false;
                try
                {
                    var c = await Request<ChallengeCalendar > ("challenge/calendar" + (month is null?"": "?month=" + month));
                    if (!window.IsVisible)return;
                    month = c.Month;
                    var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    title.Text = first.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("uk-UA"));
                    progress.Text = $"{c.Days.Count(d=>d.Status=="won")}/{c.Days.Count}";
                    grid.Children.Clear();
                    foreach (var d in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд" })grid.Children.Add(Text(d, 13, "Muted", true));
                    for (int i = 0; i < ((int)first.DayOfWeek + 6) % 7; i++)grid.Children.Add(new Border());
                    foreach (var d in c.Days)
                    {
                        var b = Action(int.Parse(d.Day[^ 2..]).ToString() + (d.Status == "won"?" ✓": ""), () => {
                            selectedDay = d.Day;
                            window.Close();
                        });
                        b.Margin = new Thickness(2);
                        b.MinHeight = 38;
                        b.IsEnabled = d.Status is "available" or "started" or "retry";
                        b.ToolTip = d.Status switch { "won" => "Перемога", "future" => "Майбутній день", "missed" => "Пропущено", "started" => "Продовжити", "retry" => "Повторити", _ => "Почати" };
                        if (d.Status == "won")
                        {
                            b.Background = B("Same");
                            b.Opacity = 1;
                        }
                        else if (d.Status == "started")b.BorderBrush = B("Accent");
                        grid.Children.Add(b);
                    }
                    cups.Children.Clear();
                    foreach (var cup in c.Cups)
                    {
                        var tile = Card(Text("🏆 " + cup, 16, "Accent", true), 6);
                        tile.Margin = new Thickness(3);
                        cups.Children.Add(tile);
                    }
                    if (c.Cups.Count == 0)cups.Children.Add(Text("Кубків ще немає.", 13, "Muted"));
                    previous.IsEnabled = first.Year > 2000 || first.Month > 1;
                    next.IsEnabled = c.Month != c.Today[..7];
                    daily = await Request<DailyStatus > ("daily");
                    if (window.IsVisible)reward.Visibility = daily.Packs.Any(p => p.Milestone == "challenge")?Visibility.Visible: Visibility.Collapsed;
                    notice.Text = "";
                }
                catch (Exception ex)
                {
                    notice.Text = ex.Message;
                    previous.IsEnabled = true;
                }
                finally
                {
                    loading = false;
                }
            }
            window.Loaded += async (_, _) => await Load();
            window.ShowDialog();
            if (navigation != homeNavigationVersion) break;
            if (claim)
            {
                challengeRewards = true;
                weeklyRewards = false;
                await ShowDaily();
                challengeRewards = false;
                continue;
            }
            if (selectedDay is null)break;
            await ShowChallengeGame(selectedDay);
        }
        Render();
    }
}
