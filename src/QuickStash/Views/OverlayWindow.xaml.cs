using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickStash.Interop;
using QuickStash.ViewModels;

namespace QuickStash.Views;

/// <summary>
/// The overlay. Created once at startup and kept alive; opening is just positioning + Show(), which keeps it well under 200 ms.
/// Code-behind only handles window mechanics (styles, placement, focus, keyboard routing); behavior lives in <see cref="OverlayViewModel"/>.
/// </summary>
public partial class OverlayWindow : Window
{
    private IntPtr _hwnd;
    private IntPtr _targetWindow;
    private OverlayViewModel? _viewModel;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        KeyDown += OnWindowKeyDown;          // bubbling: inline editors get to handle Esc first
        PreviewKeyDown += OnWindowPreviewKeyDown;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Raised when the overlay wants to close (Esc, close button, or focus lost).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>When true, losing focus does not close the overlay (e.g. while a settings dialog opened from it is up).</summary>
    public bool SuppressAutoHide { get; set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // Tool window: never appears in Alt+Tab or the taskbar.
        NativeMethods.UpdateExStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW, NativeMethods.WS_EX_APPWINDOW);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel.NoteRevealRequested -= OnNoteRevealRequested;
        }
        _viewModel = e.NewValue as OverlayViewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested += OnFocusRequested;
            _viewModel.NoteRevealRequested += OnNoteRevealRequested;
        }
    }

    private void OnNoteRevealRequested(object? sender, NoteItemViewModel note) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => NotesList.ScrollIntoView(note));

    /// <summary>Creates the HWND and renders once off-screen so the first real open is as fast as later ones.</summary>
    public void WarmUp()
    {
        ShowActivated = false;
        Left = -20000;
        Top = -20000;
        Show();
        Hide();
        ShowActivated = true;
    }

    public void ShowCentered(IntPtr targetWindow)
    {
        _targetWindow = targetWindow;
        CenterOn(targetWindow);
        Show();
        Activate();
        NativeMethods.ForceForeground(_hwnd);
    }

    /// <summary>Centers on the work area of the monitor showing <paramref name="targetWindow"/> (physical pixels, DPI-safe).</summary>
    private void CenterOn(IntPtr targetWindow)
    {
        if (_hwnd == IntPtr.Zero) return;
        var (work, scale) = NativeMethods.GetMonitorForWindow(targetWindow);
        int width = (int)Math.Round(Width * scale);
        int height = (int)Math.Round(Height * scale);
        int x = work.Left + (work.Width - width) / 2;
        int y = work.Top + (work.Height - height) / 2;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // Moving onto a monitor with a different scale resizes the window; re-center it with its new size.
    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        // WPF also raises this with an unchanged DPI (e.g. on focus changes); only a real scale change needs re-centering.
        if (e.OldDpi.PixelsPerInchX.Equals(e.NewDpi.PixelsPerInchX) || !IsVisible) return;
        Dispatcher.BeginInvoke(() => CenterOn(_targetWindow));
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible && !SuppressAutoHide) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // ───────────────────────── Keyboard ─────────────────────────

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.N:
                _viewModel.NewTopicCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.T:
                FocusElement(TopicFilterBox);
                TopicFilterBox.SelectAll();
                e.Handled = true;
                break;
            case Key.F:
                OnFocusRequested(this, FocusTarget.Search);
                e.Handled = true;
                break;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_viewModel?.HandleEscape() != true) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnFocusRequested(object? sender, FocusTarget target)
    {
        // Run after bindings/visibility have updated.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (target)
            {
                case FocusTarget.NoteInput: FocusElement(NoteInput); break;
                case FocusTarget.TopicFilter: FocusElement(TopicFilterBox); break;
                case FocusTarget.NewTopicName:
                    FocusElement(NewTopicNameBox);
                    NewTopicNameBox.SelectAll();
                    break;
                case FocusTarget.Search:
                    FocusElement(SearchBox);
                    SearchBox.SelectAll();
                    break;
            }
        });
    }

    private static void FocusElement(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element);
        if (element is TextBox box) box.CaretIndex = box.Text.Length;
    }

    // ───────────────────────── Topic list ─────────────────────────
    // Arrow keys only move the highlight; a topic opens on click or Enter, so browsing doesn't steal focus.

    private void OnTopicFilterKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveTopicHighlight(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveTopicHighlight(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                OpenHighlightedTopic();
                e.Handled = true;
                break;
        }
    }

    private void OnTopicFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        // Keep a highlight on the first match so Enter opens it.
        if (TopicList.Items.Count > 0 && (TopicList.SelectedItem is null || !TopicList.Items.Contains(TopicList.SelectedItem)))
            TopicList.SelectedIndex = 0;
    }

    private void MoveTopicHighlight(int delta)
    {
        int count = TopicList.Items.Count;
        if (count == 0) return;
        int index = TopicList.SelectedIndex < 0 ? (delta > 0 ? 0 : count - 1) : Math.Clamp(TopicList.SelectedIndex + delta, 0, count - 1);
        TopicList.SelectedIndex = index;
        TopicList.ScrollIntoView(TopicList.SelectedItem);
    }

    private void OpenHighlightedTopic()
    {
        if (_viewModel is null) return;
        if (TopicList.SelectedItem is TopicItemViewModel topic && TopicList.Items.Contains(topic))
            _viewModel.SelectTopic(topic);
        else
            _viewModel.AcceptTopicFilterCommand.Execute(null);
    }

    private void OnTopicListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenHighlightedTopic();
            e.Handled = true;
        }
    }

    private void OnTopicListClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;
        var item = ItemsControl.ContainerFromElement(TopicList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (item?.DataContext is TopicItemViewModel topic) _viewModel.SelectTopic(topic);
    }

    // ───────────────────────── Search results ─────────────────────────

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || SearchResultsList.Items.Count == 0) return;
        SearchResultsList.SelectedIndex = 0;
        if (SearchResultsList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first) FocusElement(first);
        e.Handled = true;
    }

    private void OnSearchResultClick(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(SearchResultsList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (item?.DataContext is SearchResultViewModel result) _viewModel?.OpenSearchResultCommand.Execute(result);
    }

    private void OnSearchResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchResultsList.SelectedItem is SearchResultViewModel result)
        {
            _viewModel?.OpenSearchResultCommand.Execute(result);
            e.Handled = true;
        }
    }

    private void OnTopicMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
