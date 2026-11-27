using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NeSud.Core;

namespace NeSud;

public sealed partial class MainWindow
{
    private sealed record GiftView(Button Button, Canvas Lid, FrameworkElement Prize, TextBlock Caption);

    private FrameworkElement ResourceIcon(string kind, double size = 22)
    {
        string geometry = kind switch
        {
            "hint" => "M9,18 H15 M10,21 H14 M8,15 C8,12 5,11 5,7 A7,7 0 0 1 19,7 C19,11 16,12 16,15 L16,16 H8 Z",
            "life" => "M12,21 L3,12 C-3,4 7,-1 12,6 C17,-1 27,4 21,12 Z",
            _ => "M3,3 H21 V21 H3 Z M9,3 V21 M15,3 V21 M3,9 H21 M3,15 H21"
        };
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Stretch = Stretch.Uniform, Width = size, Height = size,
            StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center };
        path.SetResourceReference(Shape.StrokeProperty, kind == "life" ? "WrongBrush" : "AccentBrush");
        return path;
    }

    private StackPanel ResourceLabel(string kind, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,8,0) };
        row.Children.Add(ResourceIcon(kind, 18));
        var text = Text(label, 14, "Text", true);
        text.Margin = new Thickness(6,0,0,0);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return row;
    }

    private GiftView MakeGift(int index)
    {
        var button = Action("", tip: $"Відкрити подарунок {index + 1}");
        button.Width = button.Height = 132;
        button.Padding = button.Margin = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Cursor = System.Windows.Input.Cursors.Hand;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        // No default button chrome, including hover and disabled backgrounds.
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        template.VisualTree = presenter;
        button.Template = template;
        System.Windows.Automation.AutomationProperties.SetName(button, $"Подарунок {index + 1}");
        var art = new Canvas { Width = 126, Height = 126, ClipToBounds = false };
        Border Piece(double x, double y, double width, double height, string color, double radius = 0, Canvas? target = null)
        {
            var shape = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius) };
            shape.SetResourceReference(Border.BackgroundProperty, color + "Brush");
            Canvas.SetLeft(shape,x); Canvas.SetTop(shape,y); (target ?? art).Children.Add(shape);
            return shape;
        }
        Piece(23,56,80,48,"Accent",5);
        Piece(56,56,14,48,"AccentText");
        var prize = new Grid { Width = 40, Height = 40, Opacity = 0, RenderTransform = new TranslateTransform(0,14) };
        Canvas.SetLeft(prize,43); Canvas.SetTop(prize,49); art.Children.Add(prize);
        var lid = new Canvas { Width = 126, Height = 126, RenderTransform = new TranslateTransform() };
        var bow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M63,47 C24,45 31,17 49,28 Q60,33 63,47 C102,45 95,17 77,28 Q66,33 63,47"),
            StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round };
        bow.SetResourceReference(Shape.StrokeProperty,"AccentBrush");
        lid.Children.Add(bow);
        Piece(17,46,92,17,"Accent",4,lid);
        Piece(56,46,14,17,"AccentText",0,lid);
        art.Children.Add(lid);
        var caption = Text("",16,"Text",true);
        caption.Width = 126; caption.TextAlignment = TextAlignment.Center;
        Canvas.SetTop(caption,110); art.Children.Add(caption);
        button.Content = art;
        return new(button,lid,prize,caption);
    }

    private async Task AnimateQuest(StackPanel body, int from, int to)
    {
        body.Width = 320;
        var title = Text("Щоденний квест", 26, "Text", true);
        title.TextAlignment = TextAlignment.Center;
        body.Children.Add(title);
        Gap(body, 14);
        var count = Text($"{from} / 7", 32, "Accent", true);
        count.TextAlignment = TextAlignment.Center;
        body.Children.Add(count);
        Gap(body, 12);
        var track = new ProgressBar { Minimum = 0, Maximum = 7, Value = from, Height = 12,
            Margin = new Thickness(16,0,16,0), BorderThickness = new Thickness(0) };
        track.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        track.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        body.Children.Add(track);
        var labels = new Canvas { Height = 34, Width = 288, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (int milestone in new[] { 1, 3, 7 })
        {
            var label = Text(milestone.ToString(), 14, milestone <= from ? "Accent" : "Muted", true);
            label.Width = 28; label.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(label, milestone * 288.0 / 7 - 14); Canvas.SetTop(label, 5);
            labels.Children.Add(label);
        }
        body.Children.Add(labels);
        if (SystemParameters.ClientAreaAnimation)
        {
            track.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            await Task.Delay(650);
        }
        track.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        track.Value = to;
        count.Text = $"{to} / 7";
        foreach (var element in labels.Children.OfType<TextBlock>())
            if (int.Parse(element.Text) <= to) element.Foreground = B("Accent");
        await Task.Delay(SystemParameters.ClientAreaAnimation ? 350 : 100);
    }

    private async Task RevealGift(GiftView gift, RewardItem item)
    {
        if (gift.Prize is Grid prize) prize.Children.Add(ResourceIcon(item.Kind,36));
        gift.Caption.Text = $"×{item.Amount}";
        var lid = (TranslateTransform)gift.Lid.RenderTransform;
        var rise = (TranslateTransform)gift.Prize.RenderTransform;
        if (!SystemParameters.ClientAreaAnimation)
        { lid.Y = -34; lid.X = 32; gift.Prize.Opacity = 1; rise.Y = -23; return; }
        var duration = TimeSpan.FromMilliseconds(450);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        lid.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-34,duration){ EasingFunction = ease });
        lid.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,32,duration){ EasingFunction = ease });
        gift.Prize.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,duration));
        rise.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(14,-23,duration){ EasingFunction = ease });
        await Task.Delay(duration);
    }
}
