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
    private void OnKey(object sender, KeyEventArgs e)
    {
        Key k = e.Key;
        var mods = Keyboard.Modifiers;
        if (k == Key.F1)
        {
            ShowHelp();
            e.Handled = true;
            return;
        }
        if (inMenu) return;
        if (Busy && k == Key.Escape)
        {
            generation?.Cancel();
            e.Handled = true;
            return;
        }
        if (mods.HasFlag(ModifierKeys.Control))
        {
            if (k == Key.N) _ = Generate(true);
            else if (k == Key.Z && mods.HasFlag(ModifierKeys.Shift)) Change(g => g.Redo());
            else if (k == Key.Z) Change(g => g.Undo());
            else if (k == Key.Y) Change(g => g.Redo());
            else return;
            e.Handled = true;
            return;
        }
        if (mods.HasFlag(ModifierKeys.Alt) || mods.HasFlag(ModifierKeys.Windows)) return;
        if (k is Key.P or Key.Escape)
        {
            TogglePause();
            e.Handled = true;
            return;
        }
        if (!Playing) return;
        int code = (int)k;
        int d = code >= (int)Key.D0 && code <= (int)Key.D9 ? code - (int)Key.D0 :
            code >= (int)Key.NumPad0 && code <= (int)Key.NumPad9 ? code - (int)Key.NumPad0 : - 1;
        if (d >= 0) Enter(d, mods.HasFlag(ModifierKeys.Shift));
        else if (k is Key.Delete or Key.Back) Enter(0);
        else if (k == Key.N || k == Key.Space && Keyboard.FocusedElement is Button b && cells.Contains(b)) ToggleNotes();
        else if (k == Key.H) Hint();
        else if (k == Key.A) Candidates();
        else if (k is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int i = G!.Selected, r = i / 9, c = i % 9;
            if (k == Key.Left) c = Math.Max(0, c - 1);
            if (k == Key.Right) c = Math.Min(8, c + 1);
            if (k == Key.Up) r = Math.Max(0, r - 1);
            if (k == Key.Down) r = Math.Min(8, r + 1);
            Select(r * 9 + c);
        }
        else return;
        e.Handled = true;
    }
}
