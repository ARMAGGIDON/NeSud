using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NeSud;

public sealed partial class MainWindow
{
    private void ShowHelp()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        At(root, Text("Як грати в NeSud", 25, "Text", true), 0, 0);

        var tabs = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        At(root, tabs, 1, 0);
        var body = Stack();
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0)
        };
        At(root, scroll, 2, 0);
        var window = Modal("Довідка", root, 790, Math.Min(760, SystemParameters.WorkArea.Height - 40));
        window.SizeToContent = SizeToContent.Manual;
        window.Height = Math.Min(740, SystemParameters.WorkArea.Height - 40);
        var close = Action("Закрити", () => window.Close(), primary: true);
        close.Margin = new Thickness(0, 12, 0, 0);
        close.IsCancel = true;
        At(root, close, 3, 0);

        string[] titles = ["Поле", "Керування", "Ресурси й бали", "Нагороди", "Щоденні ігри"];
        var buttons = new List<Button>();
        for (int i = 0; i < titles.Length; i++)
        {
            int page = i;
            var button = Action(titles[i], () => Open(page));
            button.Margin = new Thickness(0, 0, 5, 5);
            buttons.Add(button);
            tabs.Children.Add(button);
        }

        void Section(string title, string? description = null)
        {
            Gap(body, 12);
            body.Children.Add(Text(title, 18, "Text", true));
            if (description is not null)
            {
                Gap(body, 4);
                body.Children.Add(Text(description, 14, "Muted"));
            }
        }

        void Table(string[] headings, params string[][] rows)
        {
            var table = new Grid { Margin = new Thickness(0, 8, 0, 3) };
            foreach (string _ in headings) table.ColumnDefinitions.Add(new ColumnDefinition());
            int r = 0;
            foreach (var row in new[] { headings }.Concat(rows))
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < row.Length; c++)
                {
                    var label = Text(row[c], 14, r == 0 ? "Muted" : "Text", r == 0);
                    var tile = new Border
                    {
                        Background = B(r == 0 ? "Control" : "Panel"),
                        BorderBrush = B("Line"), BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(7), Child = label
                    };
                    At(table, tile, r, c);
                }
                r++;
            }
            body.Children.Add(table);
        }

        void Cards(string[] quantities)
        {
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 6) };
            string[] names = ["Підказка", "Кандидати", "Випадковий тип"];
            string[] symbols = ["✦", "▦", "?"];
            for (int k = 0; k < 3; k++)
            {
                var contents = Stack();
                var symbol = Text(symbols[k], 28, "Accent", true);
                symbol.TextAlignment = TextAlignment.Center;
                contents.Children.Add(symbol);
                var amount = Text("×" + quantities[k], 22, "Text", true);
                amount.TextAlignment = TextAlignment.Center;
                contents.Children.Add(amount);
                var name = Text(names[k], 12, "Muted");
                name.TextAlignment = TextAlignment.Center;
                contents.Children.Add(name);
                var card = Card(contents, 10);
                card.Margin = new Thickness(3);
                grid.Children.Add(card);
            }
            body.Children.Add(grid);
        }

        void BoardExample()
        {
            string[] rows = ["530070000", "600195000", "098000060", "800060003", "400803001",
                "700020006", "060000280", "000419005", "000080079"];
            var grid = new UniformGrid { Columns = 9, Rows = 9, Width = 315, Height = 315,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 8) };
            for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
            {
                bool selected = r == 0 && c == 2, wrong = r == 1 && c == 1;
                bool peer = r == 0 || c == 2 || (r < 3 && c < 3);
                string value = wrong ? "6" : rows[r][c] == '0' ? "" : rows[r][c].ToString();
                var number = Text(selected ? "1 2 4" : value, selected ? 10 : 17, wrong ? "Wrong" : "Text", !selected);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(new Border
                {
                    Child = number, Background = B(selected ? "Selected" : wrong ? "WrongBg" : peer ? "Peer" : "Panel"),
                    BorderBrush = B("StrongLine"),
                    BorderThickness = new Thickness(c == 0 ? 2 : 0, r == 0 ? 2 : 0,
                        (c + 1) % 3 == 0 ? 2 : 0.5, (r + 1) % 3 == 0 ? 2 : 0.5)
                });
            }
            body.Children.Add(grid);
            var legend = new WrapPanel();
            foreach (var (color, caption) in new[] { ("Selected", "Вибрана клітинка"), ("Peer", "Пов’язані клітинки"), ("WrongBg", "Помилка") })
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 8) };
                item.Children.Add(new Border { Width = 17, Height = 17, Background = B(color), CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 5, 0) });
                item.Children.Add(Text(caption, 12, "Muted"));
                legend.Children.Add(item);
            }
            body.Children.Add(legend);
        }

        void Open(int page)
        {
            body.Children.Clear();
            for (int k = 0; k < buttons.Count; k++)
            {
                buttons[k].Background = B(k == page ? "Accent" : "Control");
                buttons[k].Foreground = B(k == page ? "AccentText" : "Text");
            }
            switch (page)
            {
                case 0:
                    Section("Мета", "Заповніть поле 9 × 9 цифрами від 1 до 9. У кожному рядку, стовпці та блоці 3 × 3 кожна цифра має зустрічатися лише один раз.");
                    BoardExample();
                    Section("Що показано на полі", "У вибраній клітинці записано можливі кандидати 1, 2, 4. Червона цифра 6 повторює задану 6 у тому самому рядку. Нотатки не є остаточним ходом.");
                    Section("Складність", "Легка: єдині кандидати. Середня: також приховані єдині. Складна: блокування кандидатів та пари. Експерт: X-Wing і логічне виключення через суперечність.");
                    break;
                case 1:
                    Section("Клавіатура та миша", "Виберіть клітинку, а потім цифру на панелі або клавіатурі.");
                    Table(["Клавіші", "Дія"],
                        ["↑ ↓ ← →", "Вибір клітинки"], ["1–9 / NumPad", "Ввести цифру"], ["Shift + цифра", "Додати або прибрати нотатку"],
                        ["N / Space", "Режим нотаток"], ["Delete / Backspace / 0", "Стерти"], ["Ctrl+Z / Ctrl+Y", "Скасувати / повторити"],
                        ["H / A", "Підказка / кандидати"], ["P / Esc", "Пауза"], ["Ctrl+N / F1", "Нова гра / довідка"],
                        ["Tab / Enter", "Перехід / активувати кнопку"]);
                    Section("Цифрова панель", "Мала цифра у нижньому правому куті кнопки показує, скільки разів основну цифру ще потрібно правильно розмістити.");
                    break;
                case 2:
                    Section("Життя");
                    body.Children.Add(Text("♥ ♥ ♥   →   ♥ ♥ ♡   →   ♥ ♡ ♡   →   ♡ ♡ ♡", 24, "Wrong", true));
                    Section("Останній шанс", "Після втрати трьох життів є 10 секунд для запуску демонстраційної реклами. Через 8 секунд можна відновити одне життя. Скасування витрачає цей єдиний шанс.");
                    Table(["Дія", "Вплив на бали"], ["Помилка", "−75"], ["Підказка", "−150"], ["Автокандидати", "−2 за зміну"],
                        ["Автозавершення", "−20 за клітинку"], ["Відновлення життя", "−100"], ["Час", "−1 за 3 секунди"]);
                    Section("Ресурси", "На початку є одна підказка та одне використання кандидатів. Реклама доступна лише коли відповідний ресурс відсутній і в грі, і в сховищі. Автозавершення працює за умови не більше 9 порожніх клітинок без неправильних цифр.");
                    Section("Результат", "Базові бали залежать від складності. За швидке завершення додається бонус до 60%. Ідеальна перемога не містить помилок, допомоги чи відновлення.");
                    break;
                case 3:
                    Section("Одна з трьох карток", "Картки приховані та перемішані. Виберіть одну. Дві базові картки містять різні ресурси, третя має випадковий тип.");
                    Cards(["1", "1", "2"]);
                    Table(["Перемоги за день", "Можливі картки"], ["1-ша", "×1 / ×1 / ×2"], ["3-тя", "×1 / ×1 / ×2"], ["7-ма", "×3 / ×3 / ×3"]);
                    Section("Тижневий турнір", "Місце визначається сумою балів серверних звичайних і щоденних ігор, завершених за тиждень.");
                    Table(["Місце", "Можливі картки"], ["1–2", "×3 / ×3 / ×3"], ["3", "×3 / ×3 / ×2"], ["4–10", "×2 / ×2 / ×2"], ["11–30", "×1 / ×1 / ×2"]);
                    break;
                case 4:
                    Section("Календар", "Кожна дата має спільну головоломку випадкової складності: середня, складна або експерт. Пропущені дні можна пройти до кінця місяця. Звичайна гра та щоденні ігри зберігаються окремо.");
                    var calendar = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 6) };
                    for (int d = 1; d <= 31; d++)
                    {
                        var cell = Text(d + (d <= 8 ? " ✓" : d > 20 ? " ·" : ""), 14, d <= 8 ? "Accent" : d > 20 ? "Muted" : "Text", true);
                        cell.TextAlignment = TextAlignment.Center;
                        calendar.Children.Add(new Border { Background = B(d <= 8 ? "Same" : "Control"), CornerRadius = new CornerRadius(5),
                            Margin = new Thickness(2), Padding = new Thickness(5), Child = cell });
                    }
                    body.Children.Add(calendar);
                    Section("Кубок місяця", "За всі перемоги календарного місяця видається декоративний кубок. Він відображається в профілі та не дає ігрових переваг.");
                    body.Children.Add(Text("🏆   31 / 31", 26, "Accent", true));
                    Section("Нагорода за дату", "Одна з трьох прихованих карток. Кількість ресурсу в кожній визначається незалежно.");
                    Table(["Кількість", "Імовірність"], ["×1", "65%"], ["×2", "30%"], ["×3", "5%"]);
                    Section("Типи ресурсів", "95% наборів містять обидва типи, 5% лише один. Щоденні ігри входять до загальної статистики й турніру. Винагороди за звичайні перемоги 1/3/7 залишаються окремими.");
                    break;
            }
            scroll.ScrollToTop();
        }

        Open(0);
        OwnModal(window);
        window.ShowDialog();
    }
}
