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
    private InAppSurface Modal(string title, UIElement content, double width = 480, double maxHeight = 660, bool compactHeader = false) =>
        OpenSurface(title, content, width, maxHeight, false, compactHeader);

    private InAppSurface Page(string title, UIElement content, double width = 780, double maxHeight = 760) =>
        OpenSurface(title, content, width, maxHeight, true);

    private InAppSurface GiftOverlay(UIElement content) =>
        OpenSurface("Нагороди", content, 480, 530, false, floating: true);

    private InAppSurface GamePage(string title, Grid content) =>
        OpenSurface(title, content, PageShellWidth, 760, true, gameLayout: true);

    private InAppSurface OpenSurface(string title, UIElement content, double width, double maxHeight, bool page, bool compactHeader = false, bool gameLayout = false, bool floating = false)
    {
        if (Playing)
        {
            ReadClock();
            G!.Paused = true;
            SyncClock();
            Render();
            Save();
        }

        return new InAppSurface(this, title, content, width, maxHeight, page, compactHeader, gameLayout, floating);
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
        if (confirm)
        {
            var cancel = Action("Скасувати", () => window.DialogResult = false);
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
