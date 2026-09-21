using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickStash.ViewModels;

namespace QuickStash.Views;

/// <summary>First-run introduction slides. Code-behind only handles keyboard navigation, dragging and the slide animation.</summary>
public partial class OnboardingWindow : Window
{
    private readonly OnboardingViewModel _viewModel;

    public OnboardingWindow(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Dots.ItemsSource = Enumerable.Range(0, OnboardingViewModel.PageCount).ToArray();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OnboardingViewModel.Page)) AnimateSlide();
        };
        PreviewKeyDown += OnKeyDown;
        // Nothing inside is focusable by default; give the window itself focus so arrow keys and Esc reach it.
        Activated += (_, _) =>
        {
            if (Keyboard.FocusedElement is null || !IsKeyboardFocusWithin) Keyboard.Focus(this);
        };
    }

    private int _lastPage;

    /// <summary>Slides in from the side you're heading to, with a quick fade.</summary>
    private void AnimateSlide()
    {
        int direction = _viewModel.Page >= _lastPage ? 1 : -1;
        _lastPage = _viewModel.Page;
        var shift = new TranslateTransform(28 * direction, 0);
        Slides.RenderTransform = shift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        Slides.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left when _viewModel.BackCommand.CanExecute(null):
                _viewModel.BackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.SkipCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase) DragMove();
    }
}
