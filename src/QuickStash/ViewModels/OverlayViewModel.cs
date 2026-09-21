using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using QuickStash.Data;
using QuickStash.Models;
using QuickStash.Services;

namespace QuickStash.ViewModels;

public enum OverlayPage
{
    Topic,
    NewTopic,
    Search,
}

public enum FocusTarget
{
    NoteInput,
    TopicFilter,
    NewTopicName,
    Search,
}

/// <summary>Pinned-note windows, implemented by the PinManager (step 6). Optional so the overlay works without it.</summary>
internal interface IPinService
{
    void SetPinned(Note note, bool pinned);
    void NoteChanged(Note note);
    void NoteDeleted(long noteId);
}

/// <summary>State and behavior of the overlay window.</summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private const int ThumbnailDecodeWidth = 192;
    private const int ThumbnailCacheLimit = 300;

    private readonly TopicRepository _topics;
    private readonly NoteRepository _notes;
    private readonly ImageStore _images;
    private readonly DataChanges _changes;
    private readonly Dictionary<string, BitmapSource> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);

    internal OverlayViewModel(TopicRepository topics, NoteRepository notes, ImageStore images, DataChanges changes)
    {
        _topics = topics;
        _notes = notes;
        _images = images;
        _changes = changes;
        TopicsView = new ListCollectionView(Topics) { Filter = o => o is TopicItemViewModel t && t.Matches(TopicFilter) };
        _changes.Changed += OnExternalChange;
    }

    /// <summary>
    /// True while this panel is on screen. The overlay reloads everything when it opens, so it only needs live updates
    /// while open; the companion window is always live.
    /// </summary>
    internal bool IsLive { get; set; }

    /// <summary>Raised when the user wants to draw on a note's image.</summary>
    public event EventHandler<Note>? DrawRequested;

    /// <summary>Raised by the companion's capture button.</summary>
    public event EventHandler? CaptureRequested;

    /// <summary>Companion window: switch to the topic of whichever game comes to the front.</summary>
    [ObservableProperty] private bool _followGame = true;

    internal IPinService? Pins { get; set; }

    /// <summary>Asks the view to move keyboard focus.</summary>
    public event EventHandler<FocusTarget>? FocusRequested;

    /// <summary>Raised by the settings button.</summary>
    public event EventHandler? SettingsRequested;

    // ───────────────────────── Context (what was in front) ─────────────────────────

    /// <summary>Executable of the window that was in front when the overlay opened, e.g. "tld.exe".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLinkCurrentProcess), nameof(NewTopicHeading))]
    private string? _processName;

    /// <summary>Screenshot (or pasted image) waiting to be attached to the next note. Held in memory only.</summary>
    [ObservableProperty] private BitmapSource? _pendingImage;

    /// <summary>Whether the pending image will be attached when the note is saved.</summary>
    [ObservableProperty] private bool _attachPendingImage;

    // ───────────────────────── Pages ─────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopicPage), nameof(IsNewTopicPage), nameof(IsSearchPage))]
    private OverlayPage _page;

    public bool IsTopicPage => Page == OverlayPage.Topic;
    public bool IsNewTopicPage => Page == OverlayPage.NewTopic;
    public bool IsSearchPage => Page == OverlayPage.Search;

    /// <summary>Transient feedback line (e.g. "A topic with that name already exists").</summary>
    [ObservableProperty] private string? _statusMessage;

    // ───────────────────────── Topics ─────────────────────────

    public ObservableCollection<TopicItemViewModel> Topics { get; } = new();

    /// <summary>Filtered view of <see cref="Topics"/> (most recently used first).</summary>
    public ICollectionView TopicsView { get; }

    [ObservableProperty] private string _topicFilter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLinkCurrentProcess), nameof(HasSelectedTopic))]
    private TopicItemViewModel? _selectedTopic;

    public bool HasSelectedTopic => SelectedTopic is not null;

    public bool CanLinkCurrentProcess => ProcessName is not null && SelectedTopic is not null && !SelectedTopic.IsLinkedTo(ProcessName);

    partial void OnTopicFilterChanged(string value) => TopicsView.Refresh();

    // New-topic form
    [ObservableProperty] private string _newTopicName = string.Empty;
    [ObservableProperty] private bool _linkNewTopicToProcess;

    public string NewTopicHeading => ProcessName is not null && !Topics.Any(t => t.IsLinkedTo(ProcessName))
        ? $"No topic linked to {ProcessName}"
        : "New topic";

    // Topic header inline modes
    [ObservableProperty] private bool _isRenamingTopic;
    [ObservableProperty] private string _renameText = string.Empty;
    [ObservableProperty] private bool _isConfirmingTopicDelete;

    // ───────────────────────── Notes ─────────────────────────

    public ObservableCollection<NoteItemViewModel> Notes { get; } = new();

    [ObservableProperty] private string _draftText = string.Empty;

    /// <summary>Full-size image shown on top of the overlay, or null.</summary>
    [ObservableProperty] private BitmapSource? _viewerImage;

    // ───────────────────────── Lifecycle ─────────────────────────

    /// <summary>Called right before the overlay is shown with what was in front of it.</summary>
    internal void Prepare(ForegroundInfo foreground)
    {
        ProcessName = foreground.ProcessName;
        PendingImage = foreground.Screenshot;
        AttachPendingImage = foreground.Screenshot is not null;

        ViewerImage = null;
        StatusMessage = null;
        IsRenamingTopic = false;
        IsConfirmingTopicDelete = false;
        TopicFilter = string.Empty;
        if (Page == OverlayPage.Search) Page = OverlayPage.Topic; // so clearing the search doesn't navigate
        SearchText = string.Empty;
        Notes.Clear(); // always show fresh notes on open
        ReloadTopics();

        var linked = ProcessName is null ? null : Topics.FirstOrDefault(t => t.IsLinkedTo(ProcessName));
        if (linked is not null)
        {
            SelectTopic(linked);
        }
        else if (ProcessName is not null)
        {
            // Unknown game: offer to create a topic for it (the recent list stays on the left).
            SelectedTopic = null;
            Notes.Clear();
            ShowNewTopic(ProcessNameToTopicName(ProcessName));
        }
        else if (SelectedTopic is { } previous && Topics.FirstOrDefault(t => t.Id == previous.Id) is { } same)
        {
            SelectTopic(same);
        }
        else if (Topics.Count > 0)
        {
            SelectTopic(Topics[0]);
        }
        else
        {
            ShowNewTopic(string.Empty);
        }
    }

    /// <summary>Called after the overlay hides: drop in-memory images so they can be collected.</summary>
    internal void OnHidden()
    {
        PendingImage = null;
        AttachPendingImage = false;
        ViewerImage = null;
        foreach (var note in Notes) note.CancelInlineMode();
        _thumbnailCache.Clear();
    }

    /// <summary>Esc handling, innermost state first. Returns false when the overlay itself should close.</summary>
    public bool HandleEscape()
    {
        if (ViewerImage is not null)
        {
            ViewerImage = null;
            return true;
        }
        if (Notes.Any(n => n.CancelInlineMode())) return true;
        if (IsRenamingTopic || IsConfirmingTopicDelete)
        {
            IsRenamingTopic = false;
            IsConfirmingTopicDelete = false;
            return true;
        }
        if (Page == OverlayPage.Search)
        {
            ExitSearch();
            return true;
        }
        if (Page == OverlayPage.NewTopic && SelectedTopic is not null)
        {
            CancelNewTopic();
            return true;
        }
        return false;
    }

    // ───────────────────────── Topic selection ─────────────────────────

    private void ReloadTopics()
    {
        Topics.Clear();
        foreach (var topic in _topics.GetAll()) Topics.Add(new TopicItemViewModel(topic));
        OnPropertyChanged(nameof(NewTopicHeading));
    }

    /// <summary>Selects a topic (from the list, auto-detection or search) and shows its notes.</summary>
    [RelayCommand]
    public void SelectTopic(TopicItemViewModel? topic)
    {
        if (topic is null) return;
        bool changed = SelectedTopic?.Id != topic.Id || Page != OverlayPage.Topic;
        SelectedTopic = topic;
        IsRenamingTopic = false;
        IsConfirmingTopicDelete = false;
        StatusMessage = null;
        Page = OverlayPage.Topic;
        if (changed || Notes.Count == 0) LoadNotes(topic.Id);
        _topics.Touch(topic.Id);
        FocusRequested?.Invoke(this, FocusTarget.NoteInput);
    }

    /// <summary>Enter in the topic filter: open the highlighted/first match, or offer to create a topic with that name.</summary>
    [RelayCommand]
    private void AcceptTopicFilter()
    {
        var visible = TopicsView.Cast<TopicItemViewModel>().ToList();
        if (visible.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(TopicFilter)) ShowNewTopic(TopicFilter.Trim());
            return;
        }
        var target = SelectedTopic is not null && visible.Contains(SelectedTopic) ? SelectedTopic : visible[0];
        SelectTopic(target);
    }

    private void LoadNotes(long topicId)
    {
        Notes.Clear();
        foreach (var note in _notes.GetByTopic(topicId)) Notes.Add(new NoteItemViewModel(this, note));
    }

    // ───────────────────────── New topic ─────────────────────────

    [RelayCommand]
    private void NewTopic()
    {
        string prefill = !string.IsNullOrWhiteSpace(TopicFilter) ? TopicFilter.Trim()
            : ProcessName is not null && !Topics.Any(t => t.IsLinkedTo(ProcessName)) ? ProcessNameToTopicName(ProcessName)
            : string.Empty;
        ShowNewTopic(prefill);
    }

    private void ShowNewTopic(string prefill)
    {
        NewTopicName = prefill;
        LinkNewTopicToProcess = ProcessName is not null && !Topics.Any(t => t.IsLinkedTo(ProcessName));
        StatusMessage = null;
        Page = OverlayPage.NewTopic;
        OnPropertyChanged(nameof(NewTopicHeading));
        FocusRequested?.Invoke(this, FocusTarget.NewTopicName);
    }

    [RelayCommand]
    private void CreateTopic()
    {
        string name = NewTopicName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Give the topic a name.";
            return;
        }
        string? link = LinkNewTopicToProcess ? ProcessName : null;

        // Reuse an existing topic with the same name instead of failing.
        var existing = _topics.FindByName(name);
        Topic topic;
        if (existing is not null)
        {
            if (link is not null) _topics.LinkProcess(existing.Id, link);
            topic = _topics.Get(existing.Id)!;
        }
        else
        {
            topic = _topics.Create(name, link);
        }

        TopicFilter = string.Empty;
        ReloadTopics();
        SelectTopic(Topics.First(t => t.Id == topic.Id));
        _changes.Raise(this, null);
    }

    [RelayCommand]
    private void CancelNewTopic()
    {
        if (SelectedTopic is not null) SelectTopic(SelectedTopic);
    }

    /// <summary>"tld.exe" → "tld".</summary>
    internal static string ProcessNameToTopicName(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;

    // ───────────────────────── Topic header actions ─────────────────────────

    [RelayCommand]
    private void LinkCurrentProcess()
    {
        if (SelectedTopic is null || ProcessName is null) return;
        _topics.LinkProcess(SelectedTopic.Id, ProcessName);
        SelectedTopic.Topic.ProcessNames.Add(ProcessName);
        SelectedTopic.RefreshProcesses();
        OnPropertyChanged(nameof(CanLinkCurrentProcess));
        _changes.Raise(this, null);
    }

    [RelayCommand]
    private void UnlinkProcess(string? processName)
    {
        if (SelectedTopic is null || processName is null) return;
        _topics.UnlinkProcess(SelectedTopic.Id, processName);
        SelectedTopic.Topic.ProcessNames.RemoveAll(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
        SelectedTopic.RefreshProcesses();
        OnPropertyChanged(nameof(CanLinkCurrentProcess));
        _changes.Raise(this, null);
    }

    [RelayCommand]
    private void BeginRenameTopic()
    {
        if (SelectedTopic is null) return;
        IsConfirmingTopicDelete = false;
        RenameText = SelectedTopic.Name;
        IsRenamingTopic = true;
    }

    [RelayCommand]
    private void CommitRenameTopic()
    {
        if (SelectedTopic is null) return;
        string name = RenameText.Trim();
        if (name.Length == 0 || name == SelectedTopic.Name)
        {
            IsRenamingTopic = false;
            return;
        }
        var clash = _topics.FindByName(name);
        if (clash is not null && clash.Id != SelectedTopic.Id)
        {
            StatusMessage = $"A topic named \"{clash.Name}\" already exists.";
            return;
        }
        _topics.Rename(SelectedTopic.Id, name);
        SelectedTopic.Name = name;
        IsRenamingTopic = false;
        StatusMessage = null;
        TopicsView.Refresh();
        _changes.Raise(this, null);
        FocusRequested?.Invoke(this, FocusTarget.NoteInput);
    }

    [RelayCommand]
    private void CancelRenameTopic()
    {
        IsRenamingTopic = false;
        StatusMessage = null;
    }

    [RelayCommand]
    private void RequestDeleteTopic()
    {
        IsRenamingTopic = false;
        IsConfirmingTopicDelete = SelectedTopic is not null;
    }

    [RelayCommand]
    private void CancelDeleteTopic() => IsConfirmingTopicDelete = false;

    [RelayCommand]
    private void ConfirmDeleteTopic()
    {
        if (SelectedTopic is not { } topic) return;
        var noteIds = _notes.GetByTopic(topic.Id).Select(n => n.Id).ToList();
        _images.Delete(_topics.Delete(topic.Id));
        foreach (var id in noteIds) Pins?.NoteDeleted(id);
        _changes.Raise(this, null);

        IsConfirmingTopicDelete = false;
        Topics.Remove(topic);
        SelectedTopic = null;
        Notes.Clear();
        if (Topics.Count > 0) SelectTopic(Topics[0]);
        else ShowNewTopic(ProcessName is null ? string.Empty : ProcessNameToTopicName(ProcessName));
    }

    // ───────────────────────── Note input ─────────────────────────

    public string DraftHint => "Enter to save  ·  Shift+Enter new line  ·  Ctrl+V paste image";

    [RelayCommand]
    private async Task SaveNote()
    {
        if (SelectedTopic is not { } topic) return;
        string text = DraftText.Trim();
        BitmapSource? image = AttachPendingImage ? PendingImage : null;
        if (text.Length == 0 && image is null) return;

        // Clear the input immediately so the user can keep typing; restore it if saving fails.
        DraftText = string.Empty;
        PendingImage = null;
        AttachPendingImage = false;

        try
        {
            string? imagePath = image is null ? null : await _images.SaveAsync(image);
            var note = _notes.Add(topic.Id, text, imagePath);
            _topics.Touch(topic.Id);
            if (SelectedTopic?.Id == topic.Id) Notes.Insert(0, new NoteItemViewModel(this, note));
            StatusMessage = null;
            _changes.Raise(this, topic.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            Log.Error("Saving note failed", ex);
            DraftText = text;
            PendingImage = image;
            AttachPendingImage = image is not null;
            StatusMessage = "Could not save the note: " + ex.Message;
        }
    }

    /// <summary>Ctrl+V with an image on the clipboard: it replaces the pending screenshot.</summary>
    [RelayCommand]
    private void PasteImage(BitmapSource? image)
    {
        if (image is null) return;
        PendingImage = image;
        AttachPendingImage = true;
    }

    [RelayCommand]
    private void ToggleAttachPending() => AttachPendingImage = !AttachPendingImage && PendingImage is not null;

    [RelayCommand]
    private void ViewPendingImage() => ViewerImage = PendingImage;

    // ───────────────────────── Note actions (called by NoteItemViewModel) ─────────────────────────

    internal void SaveEdit(NoteItemViewModel item)
    {
        string text = item.EditText.Trim();
        bool keepsImage = item.HasImage && !item.EditRemovesImage;
        if (text.Length == 0 && !keepsImage)
        {
            StatusMessage = "A note needs text or an image. Use Delete to remove it.";
            return;
        }

        _notes.UpdateText(item.Id, text);
        item.Note.Text = text;
        item.Text = text;
        if (item.HasImage && item.EditRemovesImage)
        {
            _images.Delete(_notes.ClearImage(item.Id));
            item.OnImageRemoved();
        }
        item.IsEditing = false;
        StatusMessage = null;
        Pins?.NoteChanged(item.Note);
        _changes.Raise(this, item.Note.TopicId);
        FocusRequested?.Invoke(this, FocusTarget.NoteInput);
    }

    internal void DeleteNote(NoteItemViewModel item)
    {
        _images.Delete(_notes.Delete(item.Id));
        Pins?.NoteDeleted(item.Id);
        Notes.Remove(item);
        _changes.Raise(this, item.Note.TopicId);
        FocusRequested?.Invoke(this, FocusTarget.NoteInput);
    }

    internal void TogglePin(NoteItemViewModel item)
    {
        bool pinned = !item.IsPinned;
        _notes.SetPinned(item.Id, pinned);
        item.Note.IsPinned = pinned;
        item.IsPinned = pinned;
        Pins?.SetPinned(item.Note, pinned);
    }

    /// <summary>Called by the pin service when pins change outside the overlay list (e.g. "unpin all").</summary>
    internal void SyncPinState(long noteId, bool pinned)
    {
        var item = Notes.FirstOrDefault(n => n.Id == noteId);
        if (item is null) return;
        item.Note.IsPinned = pinned;
        item.IsPinned = pinned;
    }

    internal async Task LoadThumbnailAsync(NoteItemViewModel item)
    {
        if (item.ImagePath is not { } path) return;
        if (!_thumbnailCache.TryGetValue(path, out var thumbnail))
        {
            thumbnail = await Task.Run(() => _images.Load(path, ThumbnailDecodeWidth));
            if (thumbnail is null) return;
            if (_thumbnailCache.Count >= ThumbnailCacheLimit) _thumbnailCache.Clear();
            _thumbnailCache[path] = thumbnail;
        }
        item.SetThumbnail(thumbnail);
    }

    internal async Task OpenImageAsync(string? path)
    {
        if (path is null) return;
        var image = await Task.Run(() => _images.Load(path));
        if (image is null)
        {
            StatusMessage = "Image file is missing.";
            return;
        }
        ViewerImage = image;
    }

    [RelayCommand]
    private void CloseViewer() => ViewerImage = null;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // ───────────────────────── Shared data / companion ─────────────────────────

    /// <summary>Another panel (or a quick capture) changed data: refresh what this panel shows, keeping the selection.</summary>
    private void OnExternalChange(object? sender, long? topicId)
    {
        if (ReferenceEquals(sender, this) || !IsLive) return;

        long? selectedId = SelectedTopic?.Id;
        ReloadTopics();
        SelectedTopic = selectedId is long id ? Topics.FirstOrDefault(t => t.Id == id) : null;
        OnPropertyChanged(nameof(CanLinkCurrentProcess));

        if (SelectedTopic is null)
        {
            if (Page == OverlayPage.Topic)
            {
                Notes.Clear();
                if (Topics.Count > 0) SelectTopic(Topics[0]);
                else ShowNewTopic(string.Empty);
            }
            return;
        }
        if (topicId is null || topicId == SelectedTopic.Id) ReloadNotesKeepingState();
        if (Page == OverlayPage.Search && !string.IsNullOrWhiteSpace(SearchText)) RunSearch(SearchText);
    }

    /// <summary>Reloads the current topic's notes without losing expanded cards or an edit in progress.</summary>
    private void ReloadNotesKeepingState()
    {
        if (SelectedTopic is null || Notes.Any(n => n.IsEditing || n.IsConfirmingDelete)) return;
        var expanded = Notes.Where(n => n.IsExpanded).Select(n => n.Id).ToHashSet();
        LoadNotes(SelectedTopic.Id);
        foreach (var note in Notes) note.IsExpanded = expanded.Contains(note.Id);
    }

    /// <summary>
    /// Companion window: another program came to the front. Show which one and, when following, switch to its topic,
    /// unless the user is in the middle of something here.
    /// </summary>
    internal void Follow(ForegroundInfo foreground)
    {
        if (foreground.ProcessName is null) return;
        ProcessName = foreground.ProcessName;
        OnPropertyChanged(nameof(CanLinkCurrentProcess));
        if (!FollowGame || Page != OverlayPage.Topic || DraftText.Length > 0 || Notes.Any(n => n.IsEditing)) return;

        var linked = Topics.FirstOrDefault(t => t.IsLinkedTo(foreground.ProcessName));
        if (linked is not null && linked.Id != SelectedTopic?.Id) SelectTopic(linked);
    }

    /// <summary>Companion window: first fill, showing the linked topic of the last game if known.</summary>
    internal void Initialize(ForegroundInfo lastGame)
    {
        ReloadTopics();
        var linked = lastGame.ProcessName is null ? null : Topics.FirstOrDefault(t => t.IsLinkedTo(lastGame.ProcessName));
        ProcessName = lastGame.ProcessName;
        if (linked is not null) SelectTopic(linked);
        else if (Topics.Count > 0) SelectTopic(Topics[0]);
        else ShowNewTopic(string.Empty);
    }

    [RelayCommand]
    private void Capture() => CaptureRequested?.Invoke(this, EventArgs.Empty);

    internal void RequestDraw(NoteItemViewModel item)
    {
        if (item.HasImage) DrawRequested?.Invoke(this, item.Note);
    }

    // ───────────────────────── Global search ─────────────────────────

    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private string _searchSummary = string.Empty;

    /// <summary>Asks the view to scroll a note into view (after jumping to it from search).</summary>
    public event EventHandler<NoteItemViewModel>? NoteRevealRequested;

    /// <summary>Searches as you type; clearing the box returns to the topic.</summary>
    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            if (Page == OverlayPage.Search) ExitSearch();
            return;
        }
        RunSearch(value);
    }

    private void RunSearch(string query)
    {
        const int limit = 100;
        var results = _notes.Search(query, limit);
        SearchResults.Clear();
        foreach (var result in results) SearchResults.Add(new SearchResultViewModel(result, query));
        SearchSummary = results.Count switch
        {
            0 => $"No notes contain \"{query.Trim()}\"",
            1 => "1 note",
            >= limit => $"First {limit} notes",
            _ => $"{results.Count} notes",
        };
        Page = OverlayPage.Search;
    }

    /// <summary>Opens the note's topic and expands the note.</summary>
    [RelayCommand]
    private void OpenSearchResult(SearchResultViewModel? result)
    {
        if (result is null) return;
        var topic = Topics.FirstOrDefault(t => t.Id == result.Note.TopicId);
        if (topic is null) return;

        TopicFilter = string.Empty;
        SearchText = string.Empty; // leaves search mode
        SelectTopic(topic);
        var item = Notes.FirstOrDefault(n => n.Id == result.Note.Id);
        if (item is null) return;
        item.IsExpanded = true;
        NoteRevealRequested?.Invoke(this, item);
    }

    /// <summary>Enter in the search box opens the top result.</summary>
    [RelayCommand]
    private void OpenFirstSearchResult() => OpenSearchResult(SearchResults.FirstOrDefault());

    private void ExitSearch()
    {
        if (!string.IsNullOrEmpty(SearchText))
        {
            SearchText = string.Empty; // re-enters via OnSearchTextChanged
            return;
        }
        if (SelectedTopic is not null) SelectTopic(SelectedTopic);
        else ShowNewTopic(ProcessName is null ? string.Empty : ProcessNameToTopicName(ProcessName));
    }
}
