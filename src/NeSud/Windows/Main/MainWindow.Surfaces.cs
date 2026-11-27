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
        if (headerBack is null) return;
        bool visible = !inMenu || surfaces.LastOrDefault() is { IsPage: true };
        var visibility = visible ? Visibility.Visible : Visibility.Hidden;
        if (headerBack.Visibility == visibility) return;
        headerBack.Visibility = visibility;
        homeCue.BeginAnimation(UIElement.OpacityProperty, null);
        homeCue.Opacity = 1;
        if (visible && SystemParameters.ClientAreaAnimation)
            homeCue.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
    private bool homeRequested;
    private Action? afterHome;
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
        if (!headerBar.IsEnabled) CloseConnectionPopup();
    }

    private void NavigateHome() => NavigateHomeThen(null);

    private void NavigateHomeThen(Action? continuation)
    {
        if (closing || surfaces.LastOrDefault() is { IsPage: false }) return;
        forwardRoute = null;
        afterHome = continuation;
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
            if (!top.IsPage) { homeRequested = false; afterHome = null; return; }
            top.Close();
            // Closing a daily game can require an asynchronous server pause.
            if (top.IsVisible)
            {
                // The page may close asynchronously after pausing; its Closed handler resumes navigation.
                // A rejected close must not trigger home navigation on an unrelated future action.
                if (!top.IsClosingPending) { homeRequested = false; afterHome = null; }
                return;
            }
        }
        homeRequested = false;
        var continuation = afterHome;
        afterHome = null;
        OpenMenu();
        if (inMenu) continuation?.Invoke();
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
        public Action? Reopen { get; set; }
        public Action? RefreshPresentation { get; set; }
        public Func<bool>? EscapeAction { get; set; }
        public Action? ToggleNotesAction { get; set; }
        public Func<bool, bool>? MouseHistory { get; set; }
        public bool IsClosingPending { get; set; }
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

        public InAppSurface(MainWindow owner, string title, UIElement content, double width, double maxHeight, bool page, bool compactHeader = false, bool gameLayout = false, bool floating = false)
        {
            this.owner = owner;
            this.page = page;
            Title = title;
            preferredWidth = width;
            preferredHeight = maxHeight;
            layer = new Grid { ClipToBounds = true, Focusable = !page };
            if (page)
                layer.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
            else
                layer.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            dismiss = owner.Action("×", Close, "Закрити");
            dismiss.HorizontalAlignment = HorizontalAlignment.Right;
            dismiss.Width = 32;
            dismiss.Height = 32;
            dismiss.MinWidth = 0;
            dismiss.MinHeight = 0;
            dismiss.Padding = new Thickness(0);
            dismiss.FontSize = 20;
            dismiss.Background = Brushes.Transparent;
            dismiss.BorderBrush = Brushes.Transparent;
            dismiss.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(dismiss, "Закрити");
            if (!page && !floating)
            {
                var heading = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                heading.ColumnDefinitions.Add(new ColumnDefinition());
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (compactHeader)
                {
                    var label = owner.Text(title, 23, "Text", true);
                    label.VerticalAlignment = VerticalAlignment.Center;
                    MainWindow.At(heading, label, 0, 0);
                }
                MainWindow.At(heading, dismiss, 0, 1);
                MainWindow.At(layout, heading, 0, 0);
            }

            UIElement body = content;
            if (!floating && content is not Grid && content is not ScrollViewer)
            {
                body = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
            }

            if (page && body is FrameworkElement element)
            {
                element.MaxWidth = gameLayout ? PageShellWidth : PageContentWidth;
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            MainWindow.At(layout, body, 1, 0);
            shell = owner.Card(layout, gameLayout || floating ? 0 : page ? 18 : 14);
            if (gameLayout || floating)
            {
                shell.Background = Brushes.Transparent;
                shell.BorderThickness = new Thickness(0);
            }
            // A page container is a neutral focus anchor, not a visible interactive control.
            shell.Focusable = page;
            shell.FocusVisualStyle = null;
            KeyboardNavigation.SetIsTabStop(shell, false);
            shell.Margin = page ? new Thickness(24, 0, 24, 14) : new Thickness(12);
            shell.HorizontalAlignment = page ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            if (page) shell.MaxWidth = PageShellWidth;
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
            owner.forwardRoute = null;
            owner.surfaces.Add(this);
            owner.surfaceHost.Children.Add(layer);
            owner.UpdateHeaderNavigation();
            Fit();
            MainWindow.Fade(shell);

            owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!IsVisible) return;
                if (!page) layer.Focus();
                else shell.Focus();
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
