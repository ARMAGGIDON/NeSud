using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NeSud;

public sealed partial class MainWindow
{
    private Grid surfaceHost = null!, mainContent = null!;
    private readonly List<InAppSurface> surfaces = new();

    private void CloseSurfaces()
    {
        while (surfaces.Count > 0)
            surfaces[^1].ForceClose();
    }

    // Hosts existing synchronous workflows inside the visual tree. There is no
    // separate HWND, OS modal window, movable title bar, or owner window.
    private sealed class InAppSurface
    {
        private readonly MainWindow owner;
        private readonly Grid layer;
        private readonly Border shell;
        private readonly Button dismiss;
        private readonly double preferredWidth, preferredHeight;
        private readonly bool page;
        private DispatcherFrame? frame;
        private IInputElement? previousFocus;
        private bool? result;
        private bool closed;

        public event EventHandler? Loaded;
        public event CancelEventHandler? Closing;
        public event EventHandler? Closed;

        public event KeyEventHandler PreviewKeyDown
        {
            add => layer.PreviewKeyDown += value;
            remove => layer.PreviewKeyDown -= value;
        }

        public bool IsVisible => frame is not null && !closed;

        public bool? DialogResult
        {
            get => result;
            set
            {
                result = value;
                Close();
            }
        }

        public InAppSurface(MainWindow owner, string title, UIElement content, double width, double maxHeight, bool page)
        {
            this.owner = owner;
            this.page = page;
            preferredWidth = width;
            preferredHeight = maxHeight;
            layer = new Grid { ClipToBounds = true, Focusable = false };
            if (page)
                layer.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
            else
                layer.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            dismiss = owner.Action(page ? "‹ Назад" : "×", Close, page ? "Повернутися" : "Закрити");
            dismiss.HorizontalAlignment = page ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            dismiss.MinWidth = page ? 92 : 38;
            dismiss.MinHeight = 36;
            dismiss.FontSize = page ? 14 : 23;
            dismiss.Margin = new Thickness(0, 0, 0, 8);
            System.Windows.Automation.AutomationProperties.SetName(dismiss, page ? "Назад" : "Закрити");
            MainWindow.At(layout, dismiss, 0, 0);

            UIElement body = content;
            if (content is not Grid && content is not ScrollViewer)
            {
                body = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
            }

            if (page && body is FrameworkElement element)
            {
                element.MaxWidth = Math.Max(width, 760);
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            MainWindow.At(layout, body, 1, 0);
            shell = owner.Card(layout, page ? 18 : 14);
            shell.Margin = new Thickness(page ? 16 : 12);
            shell.HorizontalAlignment = page ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            shell.VerticalAlignment = page ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(shell, title);
            KeyboardNavigation.SetTabNavigation(shell, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(shell, KeyboardNavigationMode.Cycle);
            layer.Children.Add(shell);

            layer.SizeChanged += (_, _) => Fit();
            if (!page)
            {
                bool beganOutside = false;
                layer.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    beganOutside = ReferenceEquals(e.OriginalSource, layer);
                    if (beganOutside) e.Handled = true;
                };
                layer.PreviewMouseLeftButtonUp += (_, e) =>
                {
                    bool outside = beganOutside && ReferenceEquals(e.OriginalSource, layer);
                    beganOutside = false;
                    if (outside)
                    {
                        e.Handled = true;
                        Close();
                    }
                };
            }
        }

        private void Fit()
        {
            if (page) return;
            double width = Math.Max(0, owner.surfaceHost.ActualWidth - 24);
            double height = Math.Max(0, owner.surfaceHost.ActualHeight - 24);
            shell.Width = Math.Min(preferredWidth, width);
            shell.MaxHeight = Math.Min(preferredHeight, height);
        }

        public bool? ShowDialog()
        {
            if (owner.closing) return false;
            if (frame is not null || closed) throw new InvalidOperationException("Surface already shown.");

            previousFocus = Keyboard.FocusedElement;
            if (owner.surfaces.Count == 0)
                owner.mainContent.IsEnabled = false;
            else
                owner.surfaces[^1].layer.IsEnabled = false;

            frame = new DispatcherFrame();
            owner.surfaces.Add(this);
            owner.surfaceHost.Children.Add(layer);
            Fit();

            owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!IsVisible) return;
                dismiss.Focus();
                Loaded?.Invoke(this, EventArgs.Empty);
            }));

            Dispatcher.PushFrame(frame);
            return result;
        }

        public void Close()
        {
            if (!IsVisible || owner.surfaces.Count == 0 || owner.surfaces[^1] != this) return;
            var args = new CancelEventArgs();
            Closing?.Invoke(this, args);
            if (!args.Cancel && IsVisible) ForceClose();
        }

        public void ForceClose()
        {
            if (!IsVisible) return;
            closed = true;
            owner.surfaceHost.Children.Remove(layer);
            owner.surfaces.Remove(this);
            if (owner.surfaces.Count == 0)
                owner.mainContent.IsEnabled = true;
            else
                owner.surfaces[^1].layer.IsEnabled = true;

            frame!.Continue = false;
            Closed?.Invoke(this, EventArgs.Empty);
            if (!owner.closing && previousFocus is UIElement { IsVisible: true, IsEnabled: true } target)
                target.Focus();
        }
    }
}
