using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

    private FrameworkElement LoadingIndicator(string message)
    {
        var rotation = new RotateTransform();
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 10,2 A 8,8 0 1 1 2,10"),
            StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 20, Height = 20, Stretch = Stretch.None, RenderTransform = rotation,
            RenderTransformOrigin = new Point(.5, .5)
        };
        arc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        var indicator = new Border
        {
            Width = 30, Height = 30, Padding = new Thickness(5), Child = arc,
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent,
            ToolTip = message, Focusable = true
        };
        System.Windows.Automation.AutomationProperties.SetName(indicator, message);
        indicator.Loaded += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation)
                rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800))
                { RepeatBehavior = RepeatBehavior.Forever });
        };
        indicator.Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        return indicator;
    }

    private void SetDetailLoading(StackPanel panel, string message)
    {
        panel.Children.Clear();
        panel.Children.Add(LoadingIndicator(message));
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
