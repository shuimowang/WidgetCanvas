using System;
using System.Runtime.InteropServices;

namespace WidgetCanvas.Infrastructure.Hooks.Interop
{
    /// <summary>
    /// Win32 Hook 相关常量与原生结构定义。
    /// </summary>
    public static class NativeConstants
    {
        /// <summary>
        /// 低级鼠标 Hook：<c>WH_MOUSE_LL</c>。
        /// </summary>
        public const int WH_MOUSE_LL = 14;

        /// <summary>
        /// 低级键盘 Hook：<c>WH_KEYBOARD_LL</c>。
        /// </summary>
        public const int WH_KEYBOARD_LL = 13;

        /// <summary>
        /// 表示当前 Hook 回调收到的是有效输入消息。
        /// </summary>
        public const int HC_ACTION = 0;

        /// <summary>
        /// 全局热键消息：<c>WM_HOTKEY</c>。
        /// </summary>
        public const int WM_HOTKEY = 0x0312;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}