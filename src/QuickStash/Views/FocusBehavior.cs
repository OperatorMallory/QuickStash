using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickStash.Views;

/// <summary>
/// <c>FocusBehavior.FocusWhenVisible="True"</c>: the element takes keyboard focus whenever it becomes visible
/// (used by inline editors such as note edit and topic rename). Text boxes put the caret at the end.
/// </summary>
public static class FocusBehavior
{
    public static readonly DependencyProperty FocusWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusWhenVisible", typeof(bool), typeof(FocusBehavior), new PropertyMetadata(false, OnChanged));

    public static bool GetFocusWhenVisible(DependencyObject d) => (bool)d.GetValue(FocusWhenVisibleProperty);
    public static void SetFocusWhenVisible(DependencyObject d, bool value) => d.SetValue(FocusWhenVisibleProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.IsVisibleChanged -= OnIsVisibleChanged;
        if ((bool)e.NewValue) element.IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        var element = (UIElement)sender;
        element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Focus(element));
    }

    public static void Focus(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element);
        if (element is TextBox box) box.CaretIndex = box.Text.Length;
    }
}
