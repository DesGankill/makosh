using System.Diagnostics;
using System.Runtime.InteropServices;
using Makosh.Core;

namespace Makosh.Windows;

public sealed class WindowsDesktop : IAppHost, IShell, IKeyboard
{
    public void StartProcess(string fileName)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
        });
    }

    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    public void OpenFileOrFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    public void Send(IReadOnlyList<KeyStroke> strokes)
    {
        if (strokes.Count == 0)
        {
            return;
        }

        var inputs = new INPUT[strokes.Count];
        for (var i = 0; i < strokes.Count; i++)
        {
            inputs[i] = ToInput(strokes[i]);
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} events.");
        }
    }

    public static LocalToolServices Services(MakoshSettings settings, DeviceRegistry? devices = null)
    {
        var desktop = new WindowsDesktop();
        return new LocalToolServices
        {
            Paths = PathGuard.ForUser(settings.DataDirectory),
            Apps = desktop,
            Shell = desktop,
            Keyboard = desktop,
            Devices = devices ?? new DeviceRegistry(),
        };
    }

    internal static INPUT ToInput(KeyStroke stroke)
    {
        var input = new INPUT { type = 1 };
        if (stroke.Kind == KeyKind.Unicode)
        {
            input.U.ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = stroke.Unicode,
                dwFlags = KeyboardComposer.KeyeventfUnicode
                          | (stroke.Action == KeyAction.Up ? KeyboardComposer.KeyeventfKeyUp : 0),
            };
        }
        else
        {
            var flags = stroke.Action == KeyAction.Up ? KeyboardComposer.KeyeventfKeyUp : 0u;
            if (KeyboardComposer.IsExtended(stroke.VirtualKey))
            {
                flags |= KeyboardComposer.KeyeventfExtendedKey;
            }

            input.U.ki = new KEYBDINPUT
            {
                wVk = stroke.VirtualKey,
                wScan = 0,
                dwFlags = flags,
            };
        }

        return input;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
