#nullable enable

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using WidgetCanvas.Infrastructure.Win32;

namespace WidgetCanvas.Services
{
    internal sealed class GlobalHotkeyService : IDisposable
    {
        private const int HotkeyId = 0x5743;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;

        private readonly HwndSource _source;
        private readonly Action _onPressed;
        private bool _registered;

        public GlobalHotkeyService(Action onPressed)
        {
            ArgumentNullException.ThrowIfNull(onPressed);
            _onPressed = onPressed;
            var parameters = new HwndSourceParameters("WidgetCanvas.GlobalHotkey")
            {
                Width = 0,
                Height = 0,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = unchecked((int)0x80000000)
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        public void Apply(bool enabled, string gestureText)
        {
            Unregister();
            if (!enabled)
                return;

            HotkeyGesture gesture = HotkeyGesture.Parse(gestureText);
            if (!NativeMethods.RegisterHotKey(
                    _source.Handle,
                    HotkeyId,
                    ToNativeModifiers(gesture.Modifiers) | ModNoRepeat,
                    (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
            {
                throw new Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                    "快捷键已被其他程序占用，请换一个组合。");
            }
            _registered = true;
        }

        public void Dispose()
        {
            Unregister();
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                _onPressed();
            }
            return IntPtr.Zero;
        }

        private void Unregister()
        {
            if (!_registered)
                return;
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        private static uint ToNativeModifiers(ModifierKeys modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= ModAlt;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= ModControl;
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= ModShift;
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= ModWin;
            return result;
        }
    }

    internal readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
    {
        public static HotkeyGesture Parse(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            string[] parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new FormatException("全局快捷键至少需要一个修饰键，例如 Ctrl+Alt+W。");

            ModifierKeys modifiers = ModifierKeys.None;
            foreach (string part in parts[..^1])
            {
                modifiers |= part.ToUpperInvariant() switch
                {
                    "CTRL" or "CONTROL" => ModifierKeys.Control,
                    "ALT" => ModifierKeys.Alt,
                    "SHIFT" => ModifierKeys.Shift,
                    "WIN" or "WINDOWS" => ModifierKeys.Windows,
                    _ => throw new FormatException("无法识别快捷键修饰键：" + part)
                };
            }

            if (modifiers == ModifierKeys.None)
                throw new FormatException("全局快捷键至少需要一个修饰键。");
            if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key) ||
                IsModifierKey(key) || IsUnsupportedKey(key))
                throw new FormatException("无法识别快捷键按键：" + parts[^1]);
            return new HotkeyGesture(modifiers, key);
        }

        public static string FromInput(Key key, ModifierKeys modifiers)
        {
            if (IsUnsupportedKey(key) || IsModifierKey(key) || modifiers == ModifierKeys.None)
                throw new FormatException("请按下至少包含 Ctrl、Alt、Shift 或 Win 的组合键。");

            string[] names =
            [
                modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : string.Empty,
                modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : string.Empty,
                modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : string.Empty,
                modifiers.HasFlag(ModifierKeys.Windows) ? "Win" : string.Empty,
                key.ToString()
            ];
            return string.Join('+', names.Where(name => name.Length > 0));
        }

        private static bool IsModifierKey(Key key) => key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;

        private static bool IsUnsupportedKey(Key key) => key is
            Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed;
    }
}
