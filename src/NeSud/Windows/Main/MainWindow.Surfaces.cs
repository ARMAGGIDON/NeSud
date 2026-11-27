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
    private Grid headerBar = null!;
    private Button headerBack = null!;
    private System.Windows.Shapes.Path homeCue = null!;

    private void UpdateHomeCue()
    {
        if (homeCue is null) return;
        double opacity = !inMenu || surfaces.Count > 0 ? 1 : 0;
        if (SystemParameters.ClientAreaAnimation)
        {
            // Avoid restarting the animation during timer-driven Render calls.
            if (homeCue.Tag is double old && old == opacity) return;
            homeCue.Tag = opacity;
            homeCue.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(opacity, TimeSpan.FromMilliseconds(180)));
        }
        else homeCue.Opacity = opacity;
    }
    private bool homeRequested;
    private int homeNavigationVersion;

    private Func<Task<bool>>? beforePageNavigation;

    private async Task NavigateHeader(Action action)
    {
        var source = surfaces.LastOrDefault();
        if (source is { IsPage: false } || closing) return;
        if (beforePageNavigation is not null && !await beforePageNavigation()) return;
        if (closing || source != surfaces.LastOrDefault() || homeRequested) return;
        action();
    }

    private void UpdateHeaderNavigation()
    {
        var top = surfaces.LastOrDefault();
        headerBar.IsEnabled = top is null || top.IsPage;
        UpdateHomeCue();
        headerBack.Visibility = top is { IsPage: true } &&
            (surfaces.Count > 1 || top.Title == "Довідка") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavigateHome()
    {
        if (closing || surfaces.LastOrDefault() is { IsPage: false }) return;
        homeNavigationVersion++;
        homeRequested = true;
        ContinueHome();
    }

    private void ContinueHome()
    {
        if (!homeRequested || closing) return;
        while (surfaces.Count > 0)
        {
            var top = surfaces[^1];
            if (!top.IsPage) { homeRequested = false; return; }
            top.Close();
            // Closing a daily game can require an asynchronous server pause.
            if (top.IsVisible) return;
        }
        homeRequested = false;
        OpenMenu();
    }

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
        public bool IsPage => page;
        public string Title { get; }

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
            Title = title;
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

            dismiss = owner.Action("×", Close, "Закрити");
            dismiss.HorizontalAlignment = HorizontalAlignment.Right;
            dismiss.MinWidth = 38;
            dismiss.MinHeight = 36;
            dismiss.FontSize = 23;
            dismiss.Margin = new Thickness(0, 0, 0, 8);
            System.Windows.Automation.AutomationProperties.SetName(dismiss, "Закрити");
            if (!page) MainWindow.At(layout, dismiss, 0, 0);

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
                element.MaxWidth = 1000;
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            MainWindow.At(layout, body, 1, 0);
            shell = owner.Card(layout, page ? 18 : 14);
            shell.Margin = page ? new Thickness(24, 0, 24, 14) : new Thickness(12);
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
            owner.homeRequested = false;
            owner.surfaces.Add(this);
            owner.surfaceHost.Children.Add(layer);
            owner.UpdateHeaderNavigation();
            Fit();
            Fade(shell);

            owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!IsVisible) return;
                if (!page) dismiss.Focus();
                else layer.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
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
            owner.UpdateHeaderNavigation();
            Closed?.Invoke(this, EventArgs.Empty);
            if (owner.homeRequested && !owner.closing)
                _ = owner.Dispatcher.BeginInvoke(new Action(owner.ContinueHome));
            if (!owner.closing && previousFocus is UIElement { IsVisible: true, IsEnabled: true } target)
                target.Focus();
        }
    }
}
