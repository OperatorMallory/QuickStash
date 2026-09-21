using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStash.Models;

namespace QuickStash.ViewModels;

/// <summary>Content of one pinned-note window.</summary>
public sealed partial class PinnedNoteViewModel : ObservableObject
{
    private readonly Action<long> _unpin;

    internal PinnedNoteViewModel(Note note, string topicName, Action<long> unpin)
    {
        NoteId = note.Id;
        _unpin = unpin;
        _topicName = topicName;
        _text = note.Text;
    }

    public long NoteId { get; }

    [ObservableProperty] private string _topicName;
    [ObservableProperty] private string _text;
    [ObservableProperty] private BitmapSource? _image;

    /// <summary>True while the overlay is open: the window can be dragged and shows its unpin button.</summary>
    [ObservableProperty] private bool _isInteractive;

    [RelayCommand]
    private void Unpin() => _unpin(NoteId);
}
