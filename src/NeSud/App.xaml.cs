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

    private void OnComboBoxWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox { IsEnabled: true, IsDropDownOpen: false, IsMouseOver: true } combo ||
            combo.Items.Count == 0 || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;

        // Handle the preview event without Focus(): selection and keyboard focus are independent.
        e.Handled = true;
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

    private void OnComboBoxMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ComboBox combo) comboWheel.Remove(combo);
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
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
