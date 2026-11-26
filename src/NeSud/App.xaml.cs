using System.Windows;
using System.Threading;

namespace NeSud;

public partial class App : Application
{
    private Mutex? instance;
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
