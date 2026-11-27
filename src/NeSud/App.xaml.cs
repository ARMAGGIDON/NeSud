using System.Windows;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;

namespace NeSud;

public partial class App : Application
{
    private Mutex? instance;
    private sealed class WheelState
    {
        public int Delta;
        public long LastEvent;
    }
    private readonly ConditionalWeakTable<ComboBox, WheelState> comboWheel = new();
    private WeakReference<ComboBox>? wheelOwner;
    private Point wheelPosition;
    private long wheelTime;

    // Keep a wheel burst on its original control if a layout update moves it
    // beneath a stationary pointer. A real pointer move releases it.
    private static bool SamePointer(Point a, Point b) => (a - b).LengthSquared <= 16;

    private void OnWindowWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Window window || wheelOwner is null ||
            !wheelOwner.TryGetTarget(out var combo)) return;
        if (Environment.TickCount64 - wheelTime > 250 || !SamePointer(e.GetPosition(window), wheelPosition) ||
            !combo.IsVisible || !combo.IsEnabled || combo.IsDropDownOpen || Window.GetWindow(combo) != window ||
            Keyboard.Modifiers != ModifierKeys.None)
        {
            wheelOwner = null;
            return;
        }
        if (e.Delta != 0 && combo.Items.Count > 0) ApplyComboWheel(combo, e);
    }

    private void OnComboBoxWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox { IsEnabled: true, IsDropDownOpen: false, IsMouseOver: true } combo ||
            combo.Items.Count == 0 || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;

        ApplyComboWheel(combo, e);
    }

    private void ApplyComboWheel(ComboBox combo, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (Window.GetWindow(combo) is { } window)
        {
            wheelOwner = new WeakReference<ComboBox>(combo);
            wheelPosition = e.GetPosition(window);
            wheelTime = Environment.TickCount64;
        }
        var state = comboWheel.GetValue(combo, _ => new WheelState());
        long now = Environment.TickCount64;
        if (now - state.LastEvent > 250 || Math.Sign(state.Delta) != Math.Sign(e.Delta)) state.Delta = 0;
        state.LastEvent = now;
        state.Delta += e.Delta;
        if (Math.Abs(state.Delta) < Mouse.MouseWheelDeltaForOneLine) return;
        int direction = state.Delta > 0 ? -1 : 1;
        state.Delta = 0;
        int index = combo.SelectedIndex;
        if (index < 0) index = direction > 0 ? -1 : combo.Items.Count;
        for (int i = index + direction; i >= 0 && i < combo.Items.Count; i += direction)
        {
            if (combo.Items[i] is Separator or ComboBoxItem { IsEnabled: false } ||
                combo.ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem { IsEnabled: false }) continue;
            combo.SetCurrentValue(ComboBox.SelectedIndexProperty, i);
            break;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\NeSud.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("NeSud уже запущено.", "NeSud", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        MainWindow = new MainWindow();
        MainWindow.PreviewMouseWheel += OnWindowWheel;
        MainWindow.PreviewMouseMove += (_, args) =>
        {
            if (wheelOwner is not null && !SamePointer(args.GetPosition(MainWindow), wheelPosition)) wheelOwner = null;
        };
        MainWindow.PreviewMouseDown += (_, _) => wheelOwner = null;
        MainWindow.PreviewKeyDown += (_, _) => wheelOwner = null;
        MainWindow.Deactivated += (_, _) => wheelOwner = null;
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
