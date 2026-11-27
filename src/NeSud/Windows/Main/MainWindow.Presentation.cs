using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NeSud;

public sealed partial class MainWindow
{
    // Compact counters are reused on every render or clock tick.
    private void SetCompactMetrics(UniformGrid grid, (string Label, string Value)[] values)
    {
        if (grid.Children.Count != values.Length)
        {
            grid.Children.Clear();
            foreach (var (label, value) in values)
            {
                var item = Stack();
                item.Margin = new Thickness(0, 0, 8, 0);
                item.Children.Add(Text(label, 12, "Muted"));
                item.Children.Add(Text(value, 16, "Text", true));
                grid.Children.Add(item);
            }
        }
        for (int i = 0; i < values.Length; i++)
        {
            var item = (StackPanel)grid.Children[i];
            var label = (TextBlock)item.Children[0];
            var value = (TextBlock)item.Children[1];
            if (label.Text != values[i].Label) label.Text = values[i].Label;
            if (value.Text != values[i].Value) value.Text = values[i].Value;
        }
    }

    private void SetDetailMessage(StackPanel panel, string message)
    {
        panel.Children.Clear();
        panel.Children.Add(Text(message, 14, "Muted"));
    }

    private void SetDetailRows(StackPanel panel, params (string Label, string Value)[] values)
    {
        panel.Children.Clear();
        foreach (var (label, value) in values)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            At(row, Text(label, 14, "Muted"), 0, 0);
            var number = Text(value, 14, "Text", true);
            number.Margin = new Thickness(12, 0, 0, 0);
            At(row, number, 0, 1);
            panel.Children.Add(row);
        }
    }
}
