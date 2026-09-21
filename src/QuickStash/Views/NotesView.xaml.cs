using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using QuickStash.ViewModels;

namespace QuickStash.Views;

/// <summary>
/// Topics, notes and search, shared by the overlay and the companion window. Code-behind only handles view mechanics
/// (focus, keyboard routing, list behavior); behavior lives in <see cref="OverlayViewModel"/>.
/// </summary>
public partial class NotesView : UserControl
{
    private OverlayViewModel? _viewModel;

    public NotesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised by the header's close button.</summary>
    public event EventHandler? CloseRequested;

    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(NotesView));

    /// <summary>Extra header buttons supplied by the hosting window (placed before the settings button).</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public static readonly DependencyProperty TrailingHeaderContentProperty =
        DependencyProperty.Register(nameof(TrailingHeaderContent), typeof(object), typeof(NotesView));

    /// <summary>Extra header buttons placed just before the close button (e.g. minimize).</summary>
    public object? TrailingHeaderContent
    {
        get => GetValue(TrailingHeaderContentProperty);
        set => SetValue(TrailingHeaderContentProperty, value);
    }

    public static readonly DependencyProperty CloseToolTipProperty =
        DependencyProperty.Register(nameof(CloseToolTip), typeof(string), typeof(NotesView), new PropertyMetadata("Close"));

    public string CloseToolTip
    {
        get => (string)GetValue(CloseToolTipProperty);
        set => SetValue(CloseToolTipProperty, value);
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

    // ───────────────────────── Keyboard ─────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
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

    private FocusTarget? _pendingFocus;

    private void OnFocusRequested(object? sender, FocusTarget target)
    {
        // Only move focus inside a window that is active; never pull focus away from the game.
        // Remember the request so the window can apply it once it has been activated.
        if (Window.GetWindow(this) is not { IsActive: true })
        {
            _pendingFocus = target;
            return;
        }
        _pendingFocus = null;

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

    /// <summary>Applies the most recent focus request that arrived while the window was inactive (call after activating).</summary>
    public void ApplyPendingFocus() => OnFocusRequested(this, _pendingFocus ?? FocusTarget.NoteInput);

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
