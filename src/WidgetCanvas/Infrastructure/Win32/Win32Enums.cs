// ==============================
// File: Win32Enums.cs
// ==============================
using System;

namespace WidgetCanvas.Infrastructure.Win32
{
    [Flags]
    public enum SetWindowPosFlags : uint
    {
        SWP_NOSIZE = 0x0001u,          // 保持当前大小
        SWP_NOMOVE = 0x0002u,          // 保持当前位置
        SWP_NOZORDER = 0x0004u,        // 保持当前 Z 顺序
        SWP_NOREDRAW = 0x0008u,        // 不重绘窗口
        SWP_NOACTIVATE = 0x0010u,      // 不激活窗口
        SWP_FRAMECHANGED = 0x0020u,    // 刷新非客户区，通知边框样式已变化
        SWP_SHOWWINDOW = 0x0040u,      // 显示窗口
        SWP_HIDEWINDOW = 0x0080u,      // 隐藏窗口
        SWP_NOCOPYBITS = 0x0100u,      // 丢弃客户区原内容
        SWP_NOOWNERZORDER = 0x0200u,   // 不改变拥有者窗口的 Z 顺序
        SWP_NOSENDCHANGING = 0x0400u,  // 不发送 WM_WINDOWPOSCHANGING
        SWP_DEFERERASE = 0x2000u,      // 防止生成 WM_SYNCPAINT
        SWP_ASYNCWINDOWPOS = 0x4000u   // 异步执行窗口位置更改
    }

    /// <summary>
    /// Win32 ShowWindow 的显示命令。
    /// </summary>
    public enum ShowWindowCommands
    {
        Hide = 0,              // 隐藏窗口
        ShowNormal = 1,        // 正常显示并激活窗口
        ShowMinimized = 2,     // 显示为最小化窗口
        ShowMaximized = 3,     // 显示为最大化窗口
        ShowNoActivate = 4,    // 显示窗口但不激活
        Show = 5,              // 按当前大小和位置显示并激活
        Minimize = 6,          // 最小化窗口并激活下一个顶层窗口
        ShowMinNoActive = 7,   // 以最小化方式显示，但不激活
        ShowNA = 8,            // 按当前大小和位置显示，但不激活
        Restore = 9,           // 还原并激活窗口
        ShowDefault = 10,      // 使用启动程序指定的默认显示方式
        ForceMinimize = 11     // 强制最小化，常用于跨线程窗口
    }

    public enum GetAncestorFlags : uint
    {
        GA_PARENT = 1,         // 获取父窗口
        GA_ROOT = 2,           // 获取根窗口
        GA_ROOTOWNER = 3       // 获取根拥有者窗口
    }

    public enum GetWindowType : uint
    {
        GW_HWNDFIRST = 0,      // 获取同级中的第一个窗口
        GW_HWNDLAST = 1,       // 获取同级中的最后一个窗口
        GW_HWNDNEXT = 2,       // 获取下一个同级窗口
        GW_HWNDPREV = 3,       // 获取上一个同级窗口
        GW_OWNER = 4,          // 获取拥有者窗口
        GW_CHILD = 5,          // 获取第一个子窗口
        GW_ENABLEDPOPUP = 6    // 获取已启用的弹出窗口
    }

    [Flags]
    public enum WinEventHookFlags : uint
    {
        WINEVENT_OUTOFCONTEXT = 0x0000,
        WINEVENT_SKIPOWNTHREAD = 0x0001,
        WINEVENT_SKIPOWNPROCESS = 0x0002,
        WINEVENT_INCONTEXT = 0x0004
    }

}