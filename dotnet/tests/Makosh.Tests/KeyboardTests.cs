using Makosh.Core;
using Makosh.Windows;

namespace Makosh.Tests;

public class KeyboardTests
{
    [Fact]
    public void Unicode_text_builds_down_up_pairs()
    {
        const string text = "Привет, Makosh!";
        var strokes = KeyboardComposer.ForUnicodeText(text);
        Assert.Equal(text.Length * 2, strokes.Count);
        for (var i = 0; i < text.Length; i++)
        {
            Assert.Equal(KeyKind.Unicode, strokes[i * 2].Kind);
            Assert.Equal(text[i], strokes[i * 2].Unicode);
            Assert.Equal(KeyAction.Down, strokes[i * 2].Action);
            Assert.Equal(text[i], strokes[i * 2 + 1].Unicode);
            Assert.Equal(KeyAction.Up, strokes[i * 2 + 1].Action);
        }

        var input = WindowsDesktop.ToInput(strokes[0]);
        Assert.Equal(1u, input.type);
        Assert.Equal(0, input.U.ki.wVk);
        Assert.Equal((ushort)'П', input.U.ki.wScan);
        Assert.Equal(KeyboardComposer.KeyeventfUnicode, input.U.ki.dwFlags);
        var up = WindowsDesktop.ToInput(strokes[1]);
        Assert.Equal(KeyboardComposer.KeyeventfUnicode | KeyboardComposer.KeyeventfKeyUp, up.U.ki.dwFlags);
    }

    [Fact]
    public void Type_text_uses_keyboard_without_throwing()
    {
        var keyboard = new FakeKeyboard();
        Assert.Equal("Напечатал текст", KeyboardComposer.TypeText("Привет, Makosh!", keyboard));
        Assert.Single(keyboard.Sent);
        Assert.Equal(KeyboardComposer.ForUnicodeText("Привет, Makosh!"), keyboard.Sent[0]);
    }

    [Fact]
    public void Hotkey_ctrl_c_is_down_then_up_in_reverse()
    {
        var keyboard = new FakeKeyboard();
        Assert.Equal("Нажал ctrl c", KeyboardComposer.PressHotkey("ctrl c", keyboard));
        var strokes = keyboard.Sent.Single();
        Assert.Equal(
        [
            new KeyStroke(KeyKind.VirtualKey, '\0', 0x11, KeyAction.Down),
            new KeyStroke(KeyKind.VirtualKey, '\0', (ushort)'C', KeyAction.Down),
            new KeyStroke(KeyKind.VirtualKey, '\0', (ushort)'C', KeyAction.Up),
            new KeyStroke(KeyKind.VirtualKey, '\0', 0x11, KeyAction.Up),
        ], strokes);

        var ctrlDown = WindowsDesktop.ToInput(strokes[0]);
        Assert.Equal(0x11, ctrlDown.U.ki.wVk);
        Assert.Equal(0u, ctrlDown.U.ki.dwFlags);
    }

    [Fact]
    public void Hotkey_plus_separator_and_win_are_parsed()
    {
        var keyboard = new FakeKeyboard();
        Assert.Equal("Нажал ctrl+shift+n", KeyboardComposer.PressHotkey("ctrl+shift+n", keyboard));
        Assert.Equal(6, keyboard.Sent.Single().Count);
        Assert.Equal(0x5B, KeyboardComposer.VirtualKey("win"));
    }

    [Fact]
    public void Unknown_hotkey_is_rejected_without_sending()
    {
        var keyboard = new FakeKeyboard();
        Assert.Equal("Клавиши не разрешены: ctrl p", KeyboardComposer.PressHotkey("ctrl p", keyboard));
        Assert.Equal("Клавиши не разрешены: ", KeyboardComposer.PressHotkey("", keyboard));
        Assert.Empty(keyboard.Sent);
    }

    [Fact]
    public void Arrow_keys_are_extended_virtual_keys()
    {
        Assert.True(KeyboardComposer.IsExtended(KeyboardComposer.VirtualKey("left")));
        var input = WindowsDesktop.ToInput(new KeyStroke(KeyKind.VirtualKey, '\0', 0x25, KeyAction.Down));
        Assert.Equal(KeyboardComposer.KeyeventfExtendedKey, input.U.ki.dwFlags);
    }
}
