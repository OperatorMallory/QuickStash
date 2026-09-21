using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuickStash.ViewModels;

/// <summary>
/// The first-run introduction: a few click-through slides. Two of them ask the user to try a hotkey and tick off
/// when it actually happens (the app reports it through <see cref="MarkOverlayTried"/> / <see cref="MarkCaptureTried"/>).
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    public const int PageCount = 5;

    internal OnboardingViewModel(string overlayHotkey, string captureHotkey, string unpinHotkey)
    {
        OverlayHotkey = overlayHotkey;
        CaptureHotkey = captureHotkey;
        UnpinHotkey = unpinHotkey;
    }

    public string OverlayHotkey { get; }
    public string CaptureHotkey { get; }
    public string UnpinHotkey { get; }

    /// <summary>Raised when the user finishes or skips. The argument is true when they asked to open the companion window.</summary>
    public event EventHandler<bool>? Finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirst), nameof(IsLast), nameof(NextLabel))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _page;

    public bool IsFirst => Page == 0;
    public bool IsLast => Page == PageCount - 1;
    public string NextLabel => IsFirst ? "Get started" : IsLast ? "Done" : "Next";

    /// <summary>The user pressed the overlay hotkey while the introduction was open.</summary>
    [ObservableProperty] private bool _overlayTried;

    /// <summary>The user made a quick capture while the introduction was open.</summary>
    [ObservableProperty] private bool _captureTried;

    internal void MarkOverlayTried() => OverlayTried = true;
    internal void MarkCaptureTried() => CaptureTried = true;

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Finished?.Invoke(this, false);
        else Page++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Page--;

    private bool CanGoBack() => Page > 0;

    [RelayCommand]
    private void GoTo(object? page)
    {
        if (page is not null && int.TryParse(page.ToString(), out int index) && index is >= 0 and < PageCount) Page = index;
    }

    [RelayCommand]
    private void Skip() => Finished?.Invoke(this, false);

    [RelayCommand]
    private void OpenCompanion() => Finished?.Invoke(this, true);
}
