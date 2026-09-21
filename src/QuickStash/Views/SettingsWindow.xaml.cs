using System.Windows;
using System.Windows.Input;
using QuickStash.ViewModels;

namespace QuickStash.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) =>
        {
            if (!viewModel.IsSaved) viewModel.Revert();
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Esc cancels, except while a hotkey box is focused (it records keys itself).
        if (e.Key == Key.Escape && Keyboard.FocusedElement is not Controls.HotkeyBox)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
