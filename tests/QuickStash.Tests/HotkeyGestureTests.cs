using System.Windows.Input;
using QuickStash.Models;

namespace QuickStash.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Space", ModifierKeys.Control | ModifierKeys.Shift, Key.Space)]
    [InlineData("ctrl + shift + p", ModifierKeys.Control | ModifierKeys.Shift, Key.P)]
    [InlineData("Alt+F10", ModifierKeys.Alt, Key.F10)]
    [InlineData("Ctrl+Alt+5", ModifierKeys.Control | ModifierKeys.Alt, Key.D5)]
    [InlineData("Win+Shift+`", ModifierKeys.Windows | ModifierKeys.Shift, Key.OemTilde)]
    public void Parses_valid_gestures(string text, ModifierKeys modifiers, Key key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, key), gesture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]            // no modifier
    [InlineData("Ctrl+Shift")]       // no key
    [InlineData("Ctrl+A+B")]         // two keys
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+12")]
    public void Rejects_invalid_gestures(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void Round_trips_through_string()
    {
        foreach (var gesture in new[] { HotkeyGesture.DefaultOverlay, HotkeyGesture.DefaultUnpinAll, new HotkeyGesture(ModifierKeys.Alt, Key.D1) })
        {
            Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var parsed));
            Assert.Equal(gesture, parsed);
        }
        Assert.Equal("Ctrl+Shift+Space", HotkeyGesture.DefaultOverlay.ToString());
    }

    [Fact]
    public void Maps_to_native_modifiers()
    {
        Assert.Equal(0x2u | 0x4u, HotkeyGesture.DefaultOverlay.NativeModifiers);
        Assert.Equal(0x20u, HotkeyGesture.DefaultOverlay.VirtualKey);
    }
}
