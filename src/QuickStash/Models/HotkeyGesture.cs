using System.Windows.Input;
using QuickStash.Interop;

namespace QuickStash.Models;

/// <summary>A global hotkey such as "Ctrl+Shift+Space". Serialized as its display string.</summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static readonly HotkeyGesture DefaultOverlay = new(ModifierKeys.Control | ModifierKeys.Shift, Key.Space);
    public static readonly HotkeyGesture DefaultUnpinAll = new(ModifierKeys.Control | ModifierKeys.Shift, Key.P);

    /// <summary>A usable global hotkey needs at least one modifier and a non-modifier key.</summary>
    public bool IsValid => Modifiers != ModifierKeys.None && Key != Key.None && !IsModifierKey(Key);

    public uint NativeModifiers
    {
        get
        {
            uint m = 0;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
            if (Modifiers.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= NativeMethods.MOD_WIN;
            return m;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        if (Key == Key.None) return string.Empty;
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyToText(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; continue;
                case "shift": modifiers |= ModifierKeys.Shift; continue;
                case "alt": modifiers |= ModifierKeys.Alt; continue;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; continue;
            }
            if (key != Key.None || !TryParseKey(raw, out key)) return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return gesture.IsValid;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private static string KeyToText(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString(),
    };

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        if (text.Length == 1 && char.IsDigit(text[0])) { key = Key.D0 + (text[0] - '0'); return true; }
        switch (text)
        {
            case "`": key = Key.OemTilde; return true;
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
        }
        if (int.TryParse(text, out _)) return false; // Enum.TryParse would accept raw numbers
        return Enum.TryParse(text, ignoreCase: true, out key) && key != Key.None && !IsModifierKey(key);
    }
}
