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
    private Window Modal(string title, UIElement content, double width = 480, double maxHeight = 660)
    {
        if (Playing)
        {
            ReadClock();
            G!.Paused = true;
            SyncClock();
            Render();
            Save();
        }
        return new Window
        {
            Owner = this, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.Height,
            Width = width, MaxHeight = maxHeight, ShowInTaskbar = false, Background = B("Panel"),
            Foreground = B("Text"), Content = Card(content, 20)
        };
    }

    private bool Dialog(string title, string message, bool confirm)
    {
        var panel = Stack();
        panel.Children.Add(Text(title, 23, "Text", true));
        Gap(panel, 12);
        panel.Children.Add(Text(message, 15));
        Gap(panel, 20);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var window = Modal(title, panel);
        var owner = Application.Current.Windows.OfType<Window > ().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (owner is not null && owner != window) window.Owner = owner;
        if (confirm)
        {
            var cancel = Action("Скасувати", () => window.DialogResult = false);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(cancel);
        }
        var ok = Action(confirm ? "Підтвердити" : "Зрозуміло", () => window.DialogResult = true, primary: true);
        ok.IsDefault = !confirm;
        actions.Children.Add(ok);
        panel.Children.Add(actions);
        return window.ShowDialog() == true;
    }
}
