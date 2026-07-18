using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WidgetCanvas.Infrastructure.Win32
{
    /// <summary>
    /// Win32 P/Invoke 声明集合。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类型仅包含原生 API 声明、相关索引常量与常用句柄常量，
    /// 不包含任何业务封装或平台行为修正逻辑。
    /// </para>
    /// <para>
    /// 当前实现按 64 位环境使用，不考虑 x86 兼容。
    /// 若后续需要支持 32 位进程或 AnyCPU 混合运行，应补充对应兼容处理。
    /// </para>
    /// </remarks>
    public static class NativeMethods
    {
        /// <summary>
        /// <c>GetWindowLongPtr</c> / <c>SetWindowLongPtr</c> 使用的窗口样式索引。
        /// </summary>
        public const int GWL_STYLE = -16;

        /// <summary>
        /// <c>GetWindowLongPtr</c> / <c>SetWindowLongPtr</c> 使用的窗口扩展样式索引。
        /// </summary>
        public const int GWL_EXSTYLE = -20;

        /// <summary>
        /// <c>DwmGetWindowAttribute</c>：获取窗口扩展边框矩形的属性索引。
        /// </summary>
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        /// <summary>
        /// <c>DwmGetWindowAttribute</c>：获取窗口是否被 DWM 隐藏（例如位于其他虚拟桌面）。
        /// </summary>
        public const int DWMWA_CLOAKED = 14;

        /// <summary>
        /// <c>SetWindowPos</c>：将窗口置于所有非置顶窗口之上。
        /// </summary>
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        /// <summary>
        /// <c>SetWindowPos</c>：取消窗口置顶。
        /// </summary>
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        /// <summary>
        /// <c>SetWindowPos</c>：将窗口移动到底部。
        /// </summary>
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        /// <summary>
        /// WinEvent：前台窗口切换事件。
        /// </summary>
        public const uint EVENT_SYSTEM_FOREGROUND = 3;

        /// <summary>
        /// <c>GetDeviceCaps</c>：水平 DPI 索引。
        /// </summary>
        public const int LOGPIXELSX = 88;

        /// <summary>
        /// <c>GetDeviceCaps</c>：垂直 DPI 索引。
        /// </summary>
        public const int LOGPIXELSY = 90;

        /// <summary>
        /// <c>MonitorFromPoint</c> / <c>MonitorFromWindow</c>：获取最近显示器。
        /// </summary>
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        /// <summary>
        /// WinEvent：对象位置或大小发生变化。
        /// </summary>
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

        /// <summary>
        /// WinEvent：对象变为可见。
        /// </summary>
        public const uint EVENT_OBJECT_SHOW = 0x8002;

        /// <summary>
        /// WinEvent：对象变为不可见。
        /// </summary>
        public const uint EVENT_OBJECT_HIDE = 0x8003;

        /// <summary>
        /// WinEvent：对象被销毁。
        /// </summary>
        public const uint EVENT_OBJECT_DESTROY = 0x8001;

        /// <summary>
        /// WinEvent：窗口开始最小化。
        /// </summary>
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;

        /// <summary>
        /// WinEvent：窗口完成最小化还原。
        /// </summary>
        public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

        /// <summary>
        /// <c>SetWinEventHook</c> idObject 参数：事件源为窗口对象本身。
        /// </summary>
        public const int OBJID_WINDOW = 0;

        // === IME 控制消息 ===
        public const int WM_IME_CONTROL = 0x0283;

        public const int IMC_GETCONVERSIONMODE = 0x0001;
        public const int IMC_SETCONVERSIONMODE = 0x0002;
        public const int IMC_GETOPENSTATUS = 0x0005;
        public const int IMC_SETOPENSTATUS = 0x0006;

        // === IME 转换模式位 ===
        public const int IME_CMODE_ALPHANUMERIC = 0x0000;
        public const int IME_CMODE_NATIVE = 0x0001;  // 中文/日文/韩文模式
        public const int IME_CMODE_FULLSHAPE = 0x0008;  // 全角
        public const int IME_CMODE_SYMBOL = 0x0400;  // 中文标点

        // === SendMessageTimeout 标志 ===
        public const uint SMTO_NORMAL = 0x0000;
        public const uint SMTO_BLOCK = 0x0001;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        // === mouse_event 标志 ===
        public const int MOUSEEVENTF_WHEEL = 0x0800;

        // === 鼠标消息 ===
        public const int WM_MOUSEWHEEL = 0x020A;

        // ------------------------------
        // Window lookup / enumeration
        // ------------------------------

        /// <summary>
        /// 查找顶级窗口。
        /// </summary>
        /// <param name="lpClassName">窗口类名；传 <see langword="null"/> 表示忽略类名。</param>
        /// <param name="lpWindowName">窗口标题；传 <see langword="null"/> 表示忽略标题。</param>
        /// <returns>成功时返回窗口句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// 在指定父窗口下查找子窗口。
        /// </summary>
        /// <param name="parentHandle">父窗口句柄。</param>
        /// <param name="childAfter">从该子窗口之后开始查找；传 <see cref="IntPtr.Zero"/> 表示从第一个子窗口开始。</param>
        /// <param name="className">窗口类名；传 <see langword="null"/> 表示忽略类名。</param>
        /// <param name="windowTitle">窗口标题；传 <see langword="null"/> 表示忽略标题。</param>
        /// <returns>成功时返回子窗口句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

        /// <summary>
        /// 枚举顶级窗口时使用的回调委托。
        /// </summary>
        /// <param name="hWnd">当前枚举到的窗口句柄。</param>
        /// <param name="lParam">调用方传入的附加参数。</param>
        /// <returns><see langword="true"/> 继续枚举；<see langword="false"/> 停止枚举。</returns>
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// <c>SetWinEventHook</c> 使用的事件回调委托。
        /// </summary>
        /// <param name="hWinEventHook">WinEvent Hook 句柄。</param>
        /// <param name="eventType">触发的事件类型，对应 <c>EVENT_*</c> 常量。</param>
        /// <param name="hwnd">产生事件的窗口句柄。</param>
        /// <param name="idObject">产生事件的对象标识，如 <see cref="OBJID_WINDOW"/>。</param>
        /// <param name="idChild">子对象标识；若事件源为对象本身则为 <c>CHILDID_SELF</c>（0）。</param>
        /// <param name="dwEventThread">产生事件的线程 ID。</param>
        /// <param name="dwmsEventTime">事件发生时的系统时间（毫秒）。</param>
        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        /// <summary>
        /// 安装 WinEvent Hook，用于监听系统或进程级无障碍事件。
        /// </summary>
        /// <param name="eventMin">监听的最小事件类型,对应 <c>EVENT_*</c> 常量。</param>
        /// <param name="eventMax">监听的最大事件类型,对应 <c>EVENT_*</c> 常量。</param>
        /// <param name="hmodWinEventProc">包含回调函数的模块句柄;仅在使用 <c>WINEVENT_INCONTEXT</c>（4）将回调注入目标进程时需要提供,使用 <c>WINEVENT_OUTOFCONTEXT</c>（0）或回调位于托管代码中时,应传 <see cref="IntPtr.Zero"/>。</param>
        /// <param name="lpfnWinEventProc">事件回调委托;请保持委托实例存活直到调用 <see cref="UnhookWinEvent"/>,否则会引发崩溃。</param>
        /// <param name="idProcess">要监听的目标进程 ID;传 0 表示所有进程。</param>
        /// <param name="idThread">要监听的目标线程 ID;传 0 表示所有线程。</param>
        /// <param name="dwFlags">Hook 标志,通常为 <c>WINEVENT_OUTOFCONTEXT</c>（0）或 <c>WINEVENT_INCONTEXT</c>（4）。</param>
        /// <returns>成功时返回 Hook 句柄;失败时返回 <see cref="IntPtr.Zero"/>,可通过 <see cref="Marshal.GetLastWin32Error"/> 获取错误码。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        /// <summary>
        /// 卸载通过 <see cref="SetWinEventHook"/> 安装的 WinEvent Hook。
        /// </summary>
        /// <param name="hWinEventHook">要卸载的 Hook 句柄。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>，可通过 <see cref="Marshal.GetLastWin32Error"/> 获取错误码。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        /// <summary>
        /// 枚举系统中的所有顶级窗口。
        /// </summary>
        /// <param name="lpEnumFunc">枚举回调。</param>
        /// <param name="lParam">传递给回调的附加参数。</param>
        /// <returns><see langword="true"/> 表示调用成功；否则为 <see langword="false"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        /// <summary>
        /// 获取窗口祖先句柄。
        /// </summary>
        /// <param name="hwnd">起始窗口句柄。</param>
        /// <param name="flags">祖先查询方式。</param>
        /// <returns>成功时返回祖先窗口句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestorFlags flags);

        /// <summary>
        /// 获取与指定窗口相关联的其他窗口句柄。
        /// </summary>
        /// <param name="hWnd">目标窗口句柄。</param>
        /// <param name="uCmd">获取类型。</param>
        /// <returns>成功时返回窗口句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindow(IntPtr hWnd, GetWindowType uCmd);

        // ------------------------------
        // Window info
        // ------------------------------

        /// <summary>
        /// 获取当前前台窗口句柄。
        /// </summary>
        /// <returns>前台窗口句柄；若不存在则返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// 判断指定句柄是否为有效窗口。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <returns>是有效窗口返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        /// <summary>
        /// 判断窗口是否可见。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <returns>可见返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// 获取窗口矩形（屏幕坐标）。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="lpRect">接收窗口矩形。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// 获取窗口标题文本长度。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <returns>窗口标题长度；若无标题则返回 0。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        /// <summary>
        /// 获取窗口标题文本。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="text">接收文本的缓冲区。</param>
        /// <param name="count">缓冲区容量（含结尾空字符）。</param>
        /// <returns>复制到缓冲区的字符数，不含结尾空字符。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        /// <summary>
        /// 获取窗口类名。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="lpClassName">接收类名的缓冲区。</param>
        /// <param name="nMaxCount">缓冲区容量。</param>
        /// <returns>复制到缓冲区的字符数。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        /// <summary>
        /// 获取创建指定窗口的线程 ID，并可同时得到所属进程 ID。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="processId">接收所属进程 ID。</param>
        /// <returns>创建该窗口的线程 ID。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);


        /// <summary>
        /// 判断一个窗口是否为另一个窗口的子窗口。
        /// </summary>
        /// <param name="hWndParent">父窗口句柄。</param>
        /// <param name="hWnd">待判断的窗口句柄。</param>
        /// <returns>是子窗口返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        /// <summary>
        /// 判断窗口是否最小化。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <returns>最小化返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        /// <summary>
        /// 判断窗口是否最大化。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <returns>最大化返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        // ------------------------------
        // Window manipulate
        // ------------------------------

        /// <summary>
        /// 获取窗口长指针值。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="nIndex">要获取的索引,如 <see cref="GWL_STYLE"/>。</param>
        /// <returns>成功时返回对应值;失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        /// <remarks>
        /// 仅在 64 位进程下可用。该入口点在 32 位 user32.dll 中不存在,
        /// 32 位进程调用将抛出 <see cref="EntryPointNotFoundException"/>。
        /// </remarks>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        /// <summary>
        /// 设置窗口长指针值。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="nIndex">要设置的索引,如 <see cref="GWL_STYLE"/> 或 <see cref="GWL_EXSTYLE"/>。</param>
        /// <param name="dwNewLong">新的值。</param>
        /// <returns>
        /// 成功时返回先前的值。返回 <see cref="IntPtr.Zero"/> 不一定表示失败 ——
        /// 可能是先前值本身即为 0。需在调用前通过 <c>SetLastError(0)</c> 清空错误码,
        /// 调用后用 <see cref="Marshal.GetLastWin32Error"/> 区分"旧值为 0"与"调用失败"。
        /// </returns>
        /// <remarks>
        /// 仅在 64 位进程下可用。该入口点在 32 位 user32.dll 中不存在,
        /// 32 位进程调用将抛出 <see cref="EntryPointNotFoundException"/>。
        /// </remarks>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>
        /// 更改窗口的位置、大小、Z 顺序或显示状态。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="hWndInsertAfter">Z 顺序参考句柄，可使用 <see cref="HWND_TOPMOST"/>、<see cref="HWND_NOTOPMOST"/> 等。</param>
        /// <param name="X">新 X 坐标。</param>
        /// <param name="Y">新 Y 坐标。</param>
        /// <param name="cx">新宽度。</param>
        /// <param name="cy">新高度。</param>
        /// <param name="uFlags">控制行为的标志位。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            SetWindowPosFlags uFlags);

        /// <summary>
        /// 设置窗口显示状态。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <param name="nCmdShow">显示命令，通常使用 <c>ShowWindowCommands</c> 枚举值转换而来。</param>
        /// <returns>若窗口此前可见则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        /// <summary>
        /// 更改窗口父级。
        /// </summary>
        /// <param name="hWndChild">子窗口句柄。</param>
        /// <param name="hWndNewParent">新父窗口句柄。</param>
        /// <returns>成功时返回旧父窗口句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        /// <remarks>
        /// 该 API 在 WPF、跨线程窗口、跨进程宿主、DPI 与焦点场景下可能带来复杂副作用，
        /// 仅建议在明确理解其影响时使用。
        /// </remarks>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        /// <summary>
        /// 尝试将指定窗口置为前台窗口。
        /// </summary>
        /// <param name="hWnd">窗口句柄。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);


        // ------------------------------
        // Cursor / point / input
        // ------------------------------

        /// <summary>
        /// 获取当前光标位置（逻辑屏幕坐标）。
        /// </summary>
        /// <param name="lpPoint">接收光标位置。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        /// <summary>
        /// 获取当前光标位置（物理屏幕坐标）。
        /// </summary>
        /// <param name="lpPoint">接收光标位置。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicalCursorPos(out POINT lpPoint);

        /// <summary>
        /// 设置光标位置。
        /// </summary>
        /// <param name="X">目标 X 坐标。</param>
        /// <param name="Y">目标 Y 坐标。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int X, int Y);

        /// <summary>
        /// 合成鼠标事件。
        /// </summary>
        /// <param name="dwFlags">鼠标事件标志。</param>
        /// <param name="dx">X 坐标或移动量。</param>
        /// <param name="dy">Y 坐标或移动量。</param>
        /// <param name="dwData">滚轮增量或按键信息。</param>
        /// <param name="dwExtraInfo">附加信息。</param>
        [DllImport("user32.dll")]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

        /// <summary>
        /// 获取指定点所在的窗口句柄。
        /// </summary>
        /// <param name="p">屏幕坐标点。</param>
        /// <returns>命中的窗口句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 获取指定虚拟键的异步状态。
        /// </summary>
        /// <param name="vKey">虚拟键码。</param>
        /// <returns>返回值高位表示当前是否按下，低位表示自上次调用后是否发生过按下状态变化。</returns>
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // ------------------------------
        // DWM
        // ------------------------------

        /// <summary>
        /// 获取 DWM 窗口属性。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="dwAttribute">属性索引。</param>
        /// <param name="pvAttribute">接收属性值。</param>
        /// <param name="cbAttribute">接收缓冲区大小（字节）。</param>
        /// <returns>返回 HRESULT；0 表示成功。</returns>
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out RECT pvAttribute,
            int cbAttribute);

        /// <summary>
        /// 获取整数类型的 DWM 窗口属性。
        /// </summary>
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out int pvAttribute,
            int cbAttribute);

        // ------------------------------
        // Monitor
        // ------------------------------

        /// <summary>
        /// 获取距离指定点最近的显示器句柄。
        /// </summary>
        /// <param name="pt">屏幕坐标点。</param>
        /// <param name="dwFlags">查找选项。</param>
        /// <returns>显示器句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        /// <summary>
        /// 获取显示器信息。
        /// </summary>
        /// <param name="hMonitor">显示器句柄。</param>
        /// <param name="mi">传入时需设置结构大小，返回时包含显示器信息。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

        /// <summary>
        /// 获取与指定窗口关联的显示器句柄。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="dwFlags">查找选项。</param>
        /// <returns>显示器句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        /// <summary>
        /// 获取窗口 DPI。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口 DPI 值。</returns>
        /// <remarks>
        /// Windows 10 1607 及以上系统可用。
        /// 若目标环境不支持，可能抛出 <see cref="EntryPointNotFoundException"/>，
        /// 调用方应自行回退到其他 DPI 获取方案。
        /// </remarks>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        // ------------------------------
        // Hooks / hotkey
        // ------------------------------

        /// <summary>
        /// Windows Hook 回调委托。
        /// </summary>
        /// <param name="code">Hook 代码。</param>
        /// <param name="wParam">附加参数。</param>
        /// <param name="lParam">附加参数。</param>
        /// <returns>返回处理结果或调用链结果。</returns>
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 安装 Windows Hook。
        /// </summary>
        /// <param name="idHook">Hook 类型。</param>
        /// <param name="lpfn">回调函数。</param>
        /// <param name="hMod">模块句柄。</param>
        /// <param name="dwThreadId">线程 ID；为 0 时表示系统范围。</param>
        /// <returns>成功时返回 Hook 句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        /// <summary>
        /// 卸载 Windows Hook。
        /// </summary>
        /// <param name="hhk">Hook 句柄。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        /// <summary>
        /// 将 Hook 消息传递给下一个 Hook。
        /// </summary>
        /// <param name="hhk">当前 Hook 句柄。</param>
        /// <param name="nCode">Hook 代码。</param>
        /// <param name="wParam">附加参数。</param>
        /// <param name="lParam">附加参数。</param>
        /// <returns>下一个 Hook 的返回值。</returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 注册系统级或线程级热键。
        /// </summary>
        /// <param name="hWnd">接收热键消息的窗口句柄。</param>
        /// <param name="id">热键标识。</param>
        /// <param name="fsModifiers">修饰键组合。</param>
        /// <param name="vk">虚拟键码。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        /// <summary>
        /// 注销热键。
        /// </summary>
        /// <param name="hWnd">注册热键时使用的窗口句柄。</param>
        /// <param name="id">热键标识。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>
        /// 获取模块句柄。
        /// </summary>
        /// <param name="lpModuleName">模块名称；传 <see langword="null"/> 表示当前进程主模块。</param>
        /// <returns>成功时返回模块句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        // ------------------------------
        // GDI
        // ------------------------------

        /// <summary>
        /// 删除 GDI 对象。
        /// </summary>
        /// <param name="hObject">GDI 对象句柄。</param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 获取窗口或屏幕的设备上下文。
        /// </summary>
        /// <param name="hWnd">窗口句柄；传 <see cref="IntPtr.Zero"/> 表示整个屏幕。</param>
        /// <returns>成功时返回设备上下文句柄；失败时返回 <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        /// <summary>
        /// 释放设备上下文。
        /// </summary>
        /// <param name="hWnd">与 <see cref="GetDC"/> 对应的窗口句柄。</param>
        /// <param name="hDC">设备上下文句柄。</param>
        /// <returns>成功返回 1；失败返回 0。</returns>
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// 获取指定设备能力值。
        /// </summary>
        /// <param name="hdc">设备上下文句柄。</param>
        /// <param name="nIndex">能力索引，如 <see cref="LOGPIXELSX"/>、<see cref="LOGPIXELSY"/>。</param>
        /// <returns>对应能力值。</returns>
        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // === IME 相关（imm32.dll） ===

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        // === GUI 线程信息 ===

        /// <summary>
        /// 获取指定线程的 GUI 相关状态信息（活动窗口、焦点窗口、捕获窗口、光标等）。
        /// </summary>
        /// <param name="idThread">目标线程 ID；传 0 表示当前线程。</param>
        /// <param name="lpgui">
        /// 调用前需将 <c>cbSize</c> 字段设置为结构体大小；调用成功后包含该线程的 GUI 状态信息。
        /// </param>
        /// <returns>成功返回 <see langword="true"/>；失败返回 <see langword="false"/>，可通过 <see cref="Marshal.GetLastWin32Error"/> 获取错误码。</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        // === SendMessageTimeout（稳健版 SendMessage，跨进程避免死锁） ===

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            int Msg,
            IntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint hObject);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern uint GetClipboardSequenceNumber();
    }
}
