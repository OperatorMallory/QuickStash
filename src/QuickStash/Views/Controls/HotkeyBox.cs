using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickStash.Models;

namespace QuickStash.Views.Controls;

/// <summary>
/// A text box that records a key combination instead of text. Press e.g. Ctrl+Shift+Space and it shows "Ctrl+Shift+Space".
/// A combination needs at least one modifier. Tab still moves focus.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).ShowGesture()));

    public HotkeyBox()
    {
        // Implicit styles match exact types only; use the theme's TextBox style.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ContextMenu = null;
        GotKeyboardFocus += (_, _) => Tag = "Press a key combination…";
        LostKeyboardFocus += (_, _) => ShowGesture();
    }

    public string Gesture
    {
        get => (string)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift) return; // focus navigation

        e.Handled = true;
        var modifiers = Keyboard.Modifiers;

        if (HotkeyGesture.IsModifierKey(key) || key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            Text = modifiers != ModifierKeys.None ? ModifierText(modifiers) + "+…" : string.Empty;
            return;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.IsValid)
        {
            Text = Gesture;
            Tag = "Use Ctrl, Alt, Shift or Win with a key";
            return;
        }
        Gesture = gesture.ToString();
        ShowGesture();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        // Released the modifiers without choosing a key: go back to the current value.
        if (Keyboard.Modifiers == ModifierKeys.None) ShowGesture();
        e.Handled = true;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e) => e.Handled = true;

    private void ShowGesture() => Text = Gesture;

    private static string ModifierText(ModifierKeys modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join("+", parts);
    }
}
