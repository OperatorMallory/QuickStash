using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStash.Models;

namespace QuickStash.ViewModels;

/// <summary>A note card in the overlay list. Persistence is delegated to the owning <see cref="OverlayViewModel"/>.</summary>
public sealed partial class NoteItemViewModel : ObservableObject
{
    private readonly OverlayViewModel _owner;
    private bool _thumbnailRequested;
    private BitmapSource? _thumbnail;

    internal NoteItemViewModel(OverlayViewModel owner, Note note)
    {
        _owner = owner;
        Note = note;
        _text = note.Text;
        _isPinned = note.IsPinned;
    }

    public Note Note { get; }
    public long Id => Note.Id;
    public DateTime CreatedAt => Note.CreatedAt;
    public string? ImagePath => Note.ImagePath;
    public bool HasImage => Note.ImagePath is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasMore), nameof(HasTitle), nameof(ShowBody))]
    private string _text;

    /// <summary>First line of the note; used as its title.</summary>
    public string Title => Note.GetTitle(Text) is { Length: > 0 } title ? title : "Screenshot";

    public bool HasTitle => Note.GetTitle(Text).Length > 0;

    /// <summary>True when the full text has more than the title line (so expanding shows something new).</summary>
    public bool HasMore => Text.Trim().Length > Note.GetTitle(Text).Length;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBody), nameof(ShowActions))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBody))]
    private bool _isEditing;

    [ObservableProperty] private string _editText = string.Empty;
    [ObservableProperty] private bool _editRemovesImage;
    [ObservableProperty] private bool _isConfirmingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isPinned;

    /// <summary>Full text is shown when expanded (and not replaced by the editor).</summary>
    public bool ShowBody => IsExpanded && !IsEditing && HasMore;

    /// <summary>Action buttons stay visible when expanded or pinned; otherwise they appear on hover.</summary>
    public bool ShowActions => IsExpanded || IsPinned;

    /// <summary>Small preview, decoded off the UI thread the first time a row is displayed (virtualized lists only ask for visible rows).</summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested && HasImage)
            {
                _thumbnailRequested = true;
                _ = _owner.LoadThumbnailAsync(this);
            }
            return _thumbnail;
        }
    }

    internal void SetThumbnail(BitmapSource? image)
    {
        _thumbnail = image;
        OnPropertyChanged(nameof(Thumbnail));
    }

    internal void OnImageRemoved()
    {
        Note.ImagePath = null;
        _thumbnail = null;
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(Thumbnail));
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (IsEditing) return;
        IsExpanded = !IsExpanded;
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Text;
        EditRemovesImage = false;
        IsConfirmingDelete = false;
        IsExpanded = true;
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveEdit() => _owner.SaveEdit(this);

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void RequestDelete()
    {
        IsEditing = false;
        IsExpanded = true;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void ConfirmDelete() => _owner.DeleteNote(this);

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void TogglePin() => _owner.TogglePin(this);

    [RelayCommand]
    private void Draw() => _owner.RequestDraw(this);

    [RelayCommand]
    private Task OpenImage() => _owner.OpenImageAsync(ImagePath);

    /// <summary>Leaves any inline mode (edit / delete confirmation). Returns true if there was one.</summary>
    internal bool CancelInlineMode()
    {
        if (!IsEditing && !IsConfirmingDelete) return false;
        IsEditing = false;
        IsConfirmingDelete = false;
        return true;
    }
}
