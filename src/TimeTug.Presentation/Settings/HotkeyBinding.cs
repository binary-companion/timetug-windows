namespace TimeTug.Presentation.Settings;

/// <summary>The modifier keys of a shortcut. The values are the ones Windows uses (<c>MOD_ALT</c> and so on), so they pass through unchanged.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>Why a shortcut cannot be used (None: it can).</summary>
public enum HotkeyReason
{
    None,

    /// <summary>Another program already holds this shortcut.</summary>
    OtherAppHoldsIt,

    /// <summary>The shortcut has no modifier key; a bare key would be taken from every program.</summary>
    NoModifier,

    /// <summary>The key is missing, or is itself a modifier key.</summary>
    NoKey,
}

/// <summary>A global shortcut: modifiers plus a Windows virtual-key code.</summary>
public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly (int Code, string Name)[] NamedKeys =
    [
        (0x08, "Backspace"), (0x09, "Tab"), (0x0D, "Enter"), (0x1B, "Escape"), (0x20, "Space"),
        (0x21, "PageUp"), (0x22, "PageDown"), (0x23, "End"), (0x24, "Home"),
        (0x25, "Left"), (0x26, "Up"), (0x27, "Right"), (0x28, "Down"),
        (0x2D, "Insert"), (0x2E, "Delete"),
        (0xBA, ";"), (0xBB, "="), (0xBC, ","), (0xBD, "-"), (0xBE, "."), (0xBF, "/"), (0xC0, "`"),
        (0xDB, "["), (0xDC, "\\"), (0xDD, "]"), (0xDE, "'"),
    ];

    private const string CodePrefix = "Key0x";

    /// <summary>The stable text form used for storage and for showing the shortcut: modifiers in the order Ctrl, Alt, Shift, Win, then the key ("Ctrl+Alt+T").</summary>
    public string ToText()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    public override string ToString() => ToText();

    /// <summary>Reads the text form back (case and spaces do not matter). False, with <c>default</c>, when the text is not a shortcut.</summary>
    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = ModifierOf(part);
            if (modifier == HotkeyModifiers.None || modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryKeyOf(parts[^1], out var key))
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers, key);
        return true;
    }

    /// <summary>None when the shortcut can be registered, otherwise why not. Only the shape is checked here; whether another program holds it is known only to Windows.</summary>
    public HotkeyReason Validate()
    {
        if (VirtualKey is <= 0 or > 0xFE || IsModifierKey(VirtualKey))
        {
            return HotkeyReason.NoKey;
        }

        return Modifiers == HotkeyModifiers.None ? HotkeyReason.NoModifier : HotkeyReason.None;
    }

    private static bool IsModifierKey(int code) => code is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    private static HotkeyModifiers ModifierOf(string text) => text.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Control,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
        _ => HotkeyModifiers.None,
    };

    private static string KeyName(int code)
    {
        if (code is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)code).ToString();
        }

        if (code is >= 0x70 and <= 0x87)
        {
            return "F" + (code - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (code is >= 0x60 and <= 0x69)
        {
            return "Numpad" + (code - 0x60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (named, name) in NamedKeys)
        {
            if (named == code)
            {
                return name;
            }
        }

        return CodePrefix + code.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryKeyOf(string text, out int code)
    {
        code = 0;
        var upper = text.ToUpperInvariant();
        if (upper.Length == 1 && (upper[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            code = upper[0];
            return true;
        }

        if (upper.Length is >= 2 and <= 3 && upper[0] == 'F' && int.TryParse(upper.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var function) && function is >= 1 and <= 24)
        {
            code = 0x6F + function;
            return true;
        }

        if (upper.Length == 7 && upper.StartsWith("NUMPAD", StringComparison.Ordinal) && upper[6] is >= '0' and <= '9')
        {
            code = 0x60 + (upper[6] - '0');
            return true;
        }

        foreach (var (named, name) in NamedKeys)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                code = named;
                return true;
            }
        }

        if (upper.StartsWith(CodePrefix.ToUpperInvariant(), StringComparison.Ordinal)
            && int.TryParse(upper.AsSpan(CodePrefix.Length), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var hex)
            && hex is > 0 and <= 0xFE)
        {
            code = hex;
            return true;
        }

        return false;
    }
}
