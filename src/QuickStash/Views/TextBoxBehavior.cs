using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QuickStash.Services;

namespace QuickStash.Views;

/// <summary>
/// Attached behaviors for text boxes, so views stay declarative:
/// <list type="bullet">
/// <item><c>EnterCommand</c>: Enter executes the command; Shift+Enter inserts a new line (when AcceptsReturn).</item>
/// <item><c>EscapeCommand</c>: Esc executes the command (handled, so the overlay doesn't also close).</item>
/// <item><c>PasteImageCommand</c>: Ctrl+V with an image on the clipboard executes the command with the image instead of pasting text.</item>
/// </list>
/// </summary>
public static class TextBoxBehavior
{
    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand", typeof(ICommand), typeof(TextBoxBehavior), new PropertyMetadata(null, OnKeyCommandChanged));

    public static ICommand? GetEnterCommand(DependencyObject d) => (ICommand?)d.GetValue(EnterCommandProperty);
    public static void SetEnterCommand(DependencyObject d, ICommand? value) => d.SetValue(EnterCommandProperty, value);

    public static readonly DependencyProperty EscapeCommandProperty = DependencyProperty.RegisterAttached(
        "EscapeCommand", typeof(ICommand), typeof(TextBoxBehavior), new PropertyMetadata(null, OnKeyCommandChanged));

    public static ICommand? GetEscapeCommand(DependencyObject d) => (ICommand?)d.GetValue(EscapeCommandProperty);
    public static void SetEscapeCommand(DependencyObject d, ICommand? value) => d.SetValue(EscapeCommandProperty, value);

    public static readonly DependencyProperty PasteImageCommandProperty = DependencyProperty.RegisterAttached(
        "PasteImageCommand", typeof(ICommand), typeof(TextBoxBehavior), new PropertyMetadata(null, OnPasteCommandChanged));

    public static ICommand? GetPasteImageCommand(DependencyObject d) => (ICommand?)d.GetValue(PasteImageCommandProperty);
    public static void SetPasteImageCommand(DependencyObject d, ICommand? value) => d.SetValue(PasteImageCommandProperty, value);

    private static void OnKeyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        box.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == Key.Enter && GetEnterCommand(box) is { } enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (box.AcceptsReturn) return; // let the TextBox insert the new line
                e.Handled = true;
                return;
            }
            e.Handled = true;
            // Push the current text to the view model first (bindings may update on LostFocus).
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (enter.CanExecute(null)) enter.Execute(null);
        }
        else if (e.Key == Key.Escape && GetEscapeCommand(box) is { } escape && escape.CanExecute(null))
        {
            e.Handled = true;
            escape.Execute(null);
        }
    }

    private static void OnPasteCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        CommandManager.RemovePreviewExecutedHandler(box, OnPreviewExecuted);
        CommandManager.AddPreviewExecutedHandler(box, OnPreviewExecuted);
        CommandManager.RemovePreviewCanExecuteHandler(box, OnPreviewCanExecute);
        CommandManager.AddPreviewCanExecuteHandler(box, OnPreviewCanExecute);
    }

    // A TextBox reports Paste as unavailable when the clipboard has no text; enable it for images.
    private static void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || GetPasteImageCommand((DependencyObject)sender) is null) return;
        try
        {
            if (!Clipboard.ContainsImage() && !Clipboard.ContainsData("PNG")) return;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return; // clipboard busy
        }
        e.CanExecute = true;
        e.Handled = true;
    }

    private static void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || GetPasteImageCommand((DependencyObject)sender) is not { } command) return;
        BitmapSource? image = ClipboardImage.TryGet();
        if (image is null) return; // plain text: normal paste
        e.Handled = true;
        if (command.CanExecute(image)) command.Execute(image);
    }
}
