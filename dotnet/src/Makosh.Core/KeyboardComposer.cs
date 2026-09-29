namespace Makosh.Core;

public enum KeyKind
{
    Unicode,
    VirtualKey,
}

public enum KeyAction
{
    Down,
    Up,
}

public readonly record struct KeyStroke(KeyKind Kind, char Unicode, ushort VirtualKey, KeyAction Action);

public static class KeyboardComposer
{
    public const uint KeyeventfExtendedKey = 0x0001;
    public const uint KeyeventfKeyUp = 0x0002;
    public const uint KeyeventfUnicode = 0x0004;

    public static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "ctrl", "alt", "shift", "win", "tab", "enter", "esc", "space", "backspace", "delete",
        "up", "down", "left", "right",
        "c", "v", "x", "z", "a", "s", "f", "n", "w", "t", "l",
    };

    public static IReadOnlyList<KeyStroke> ForUnicodeText(string text)
    {
        var strokes = new List<KeyStroke>(text.Length * 2);
        foreach (var ch in text)
        {
            strokes.Add(new KeyStroke(KeyKind.Unicode, ch, 0, KeyAction.Down));
            strokes.Add(new KeyStroke(KeyKind.Unicode, ch, 0, KeyAction.Up));
        }

        return strokes;
    }

    public static string TypeText(string text, IKeyboard keyboard)
    {
        keyboard.Send(ForUnicodeText(text));
        return "Напечатал текст";
    }

    public static string PressHotkey(string keys, IKeyboard keyboard)
    {
        var parts = keys.Replace("+", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant())
            .ToList();
        if (parts.Count == 0 || parts.Any(part => !AllowedKeys.Contains(part)))
        {
            return $"Клавиши не разрешены: {keys}";
        }

        var strokes = new List<KeyStroke>(parts.Count * 2);
        foreach (var part in parts)
        {
            strokes.Add(new KeyStroke(KeyKind.VirtualKey, '\0', VirtualKey(part), KeyAction.Down));
        }

        for (var i = parts.Count - 1; i >= 0; i--)
        {
            strokes.Add(new KeyStroke(KeyKind.VirtualKey, '\0', VirtualKey(parts[i]), KeyAction.Up));
        }

        keyboard.Send(strokes);
        return $"Нажал {keys}";
    }

    public static ushort VirtualKey(string name) => name switch
    {
        "ctrl" => 0x11,
        "alt" => 0x12,
        "shift" => 0x10,
        "win" => 0x5B,
        "tab" => 0x09,
        "enter" => 0x0D,
        "esc" => 0x1B,
        "space" => 0x20,
        "backspace" => 0x08,
        "delete" => 0x2E,
        "up" => 0x26,
        "down" => 0x28,
        "left" => 0x25,
        "right" => 0x27,
        { Length: 1 } letter => (ushort)char.ToUpperInvariant(letter[0]),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown key."),
    };

    public static bool IsExtended(ushort vk) =>
        vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x2E or 0x5B;
}
