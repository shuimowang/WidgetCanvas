using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WidgetCanvas.Infrastructure.Win32
{
    /// <summary>
    /// 提供对 WPF <see cref="Window"/> 与 Win32 HWND 的常用桥接、样式控制、
    /// 窗口边界读取、显示器信息查询以及前台/置顶等辅助操作。
    /// </summary>
    public static class WindowHelper
    {

        /// <summary>
        /// 获取指定 WPF 窗口对应的 HWND。
        /// </summary>
        /// <param name="window">目标窗口。</param>
        /// <returns>窗口 HWND。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="window"/> 为 <see langword="null"/>。</exception>
        public static IntPtr GetHandle(this Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            return new WindowInteropHelper(window).Handle;
        }

        /// <summary>
        /// 将 WPF 窗口放置到鼠标所在屏幕中。
        /// </summary>
        /// <param name="window">目标窗口。调用时必须已经创建 HWND。</param>
        /// <param name="location">
        /// 窗口定位方式。除 <see cref="WindowLocation.FullScreen"/> 使用显示器完整边界外，
        /// 其余方式均使用工作区。
        /// </param>
        /// <remarks>
        /// 建议在 <see cref="Window.SourceInitialized"/> 事件中调用。
        /// 方法使用物理像素定位，并在窗口加载完成后按最终尺寸校正一次，
        /// 可正确处理多显示器、不同 DPI 和 <see cref="Window.SizeToContent"/> 窗口。
        /// </remarks>
        public static void SetLocation(Window window, WindowLocation location)
        {
            SetLocation(window, location, null);
        }

        /// <summary>
        /// 设置 WPF 窗口的位置，并可从物理像素数据恢复大小。
        /// </summary>
        /// <param name="window">目标窗口。调用时必须已经创建 HWND。</param>
        /// <param name="location">窗口定位方式。</param>
        /// <param name="position">
        /// 物理像素矩形或大小，格式为 <c>Left,Top,Width,Height</c> 或 <c>Width,Height</c>。
        /// 两值格式的每一项还可使用工作区百分比，例如 <c>50%,70%</c> 或 <c>800,60%</c>。
        /// 自动模式配合四值格式恢复完整边界；其他情况只恢复宽高，再按
        /// <paramref name="location"/> 定位。为空或格式无效时保持当前大小。
        /// <see cref="WindowLocation.FullScreen"/> 会忽略本参数。
        /// </param>
        public static void SetLocation(
            Window window,
            WindowLocation location,
            string? position)
        {
            ArgumentNullException.ThrowIfNull(window);

            if (!Enum.IsDefined(location))
                throw new ArgumentOutOfRangeException(nameof(location));

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "窗口句柄尚未创建，请在 SourceInitialized 事件中调用此方法。");
            }

            bool hasBounds = TryParsePosition(position, out System.Drawing.Rectangle bounds);

            if (location == WindowLocation.Auto)
            {
                if (hasBounds)
                {
                    ApplyWithLayoutCorrection(
                        window,
                        () => SetBoundsCore(hwnd, bounds));
                    return;
                }

                location = WindowLocation.Center;
            }

            POINT mouse = GetMousePhysicalPosition();
            if (!TryGetMonitorBoundsFromPoint(
                    mouse.X,
                    mouse.Y,
                    out RECT monitorArea,
                    out RECT workArea))
            {
                return;
            }

            if (location == WindowLocation.FullScreen)
            {
                var monitorBounds = new System.Drawing.Rectangle(
                    monitorArea.Left,
                    monitorArea.Top,
                    monitorArea.Width,
                    monitorArea.Height);
                ApplyWithLayoutCorrection(
                    window,
                    () => SetBoundsCore(hwnd, monitorBounds));
                return;
            }

            System.Drawing.Size size = default;
            bool hasSize = !hasBounds && TryParseSize(position, workArea, out size);
            System.Drawing.Size? requestedSize = hasBounds
                ? bounds.Size
                : hasSize
                    ? size
                    : null;

            ApplyWithLayoutCorrection(
                window,
                () => SetLocationCore(hwnd, location, mouse, workArea, requestedSize));
        }

        private static bool TryParsePosition(
            string? position,
            out System.Drawing.Rectangle bounds)
        {
            bounds = default;
            if (string.IsNullOrWhiteSpace(position))
                return false;

            try
            {
                TypeConverter converter =
                    TypeDescriptor.GetConverter(typeof(System.Drawing.Rectangle));
                if (converter.ConvertFromInvariantString(position) is not
                    System.Drawing.Rectangle parsed ||
                    parsed.Width <= 0 ||
                    parsed.Height <= 0)
                {
                    return false;
                }

                bounds = parsed;
                return true;
            }
            catch (Exception ex) when (
                ex is ArgumentException or FormatException or NotSupportedException)
            {
                return false;
            }
        }

        private static bool TryParseSize(
            string? position,
            RECT workArea,
            out System.Drawing.Size size)
        {
            size = default;
            if (string.IsNullOrWhiteSpace(position))
                return false;

            string[] parts = position.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !TryParseSizePart(parts[0], workArea.Width, out int width) ||
                !TryParseSizePart(parts[1], workArea.Height, out int height))
                return false;

            size = new System.Drawing.Size(width, height);
            return true;
        }

        private static bool TryParseSizePart(
            string value,
            int availableLength,
            out int length)
        {
            length = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!value.EndsWith('%'))
            {
                return int.TryParse(
                           value,
                           NumberStyles.Integer,
                           CultureInfo.InvariantCulture,
                           out length) &&
                       length > 0;
            }

            string percentageText = value[..^1].Trim();
            if (!double.TryParse(
                    percentageText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double percentage) ||
                !(percentage > 0 && percentage <= 100))
            {
                return false;
            }

            length = Math.Max(
                1,
                (int)Math.Round(
                    availableLength * percentage / 100,
                    MidpointRounding.AwayFromZero));
            return true;
        }

        private static void ApplyWithLayoutCorrection(Window window, Action apply)
        {
            apply();
            window.Dispatcher.BeginInvoke(apply, DispatcherPriority.ContextIdle);
        }

        private static void SetBoundsCore(
            IntPtr hwnd,
            System.Drawing.Rectangle bounds)
        {
            if (!TryGetWindowFrameInsets(
                    hwnd,
                    out _,
                    out int leftInset,
                    out int topInset,
                    out int rightInset,
                    out int bottomInset))
            {
                return;
            }

            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                bounds.Left - leftInset,
                bounds.Top - topInset,
                bounds.Width + leftInset + rightInset,
                bounds.Height + topInset + bottomInset,
                SetWindowPosFlags.SWP_NOZORDER |
                SetWindowPosFlags.SWP_NOACTIVATE);
        }

        private static void SetLocationCore(
            IntPtr hwnd,
            WindowLocation location,
            POINT mouse,
            RECT workArea,
            System.Drawing.Size? requestedSize)
        {
            if (!TryGetWindowFrameInsets(
                    hwnd,
                    out RECT visibleBounds,
                    out int leftInset,
                    out int topInset,
                    out int rightInset,
                    out int bottomInset))
            {
                return;
            }

            const int mouseOffset = 12;
            int width = requestedSize?.Width ?? visibleBounds.Width;
            int height = requestedSize?.Height ?? visibleBounds.Height;
            int left;
            int top;

            switch (location)
            {
                case WindowLocation.Center:
                    left = workArea.Left + (workArea.Width - width) / 2;
                    top = workArea.Top + (workArea.Height - height) / 2;
                    break;

                case WindowLocation.NearMouse:
                    left = mouse.X + mouseOffset;
                    top = mouse.Y + mouseOffset;

                    if (left + width > workArea.Right)
                        left = mouse.X - mouseOffset - width;
                    if (top + height > workArea.Bottom)
                        top = mouse.Y - mouseOffset - height;

                    int maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
                    int maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);
                    left = Math.Clamp(left, workArea.Left, maximumLeft);
                    top = Math.Clamp(top, workArea.Top, maximumTop);
                    break;

                case WindowLocation.TopLeft:
                    left = workArea.Left;
                    top = workArea.Top;
                    break;

                case WindowLocation.TopCenter:
                    left = workArea.Left + (workArea.Width - width) / 2;
                    top = workArea.Top;
                    break;

                case WindowLocation.TopRight:
                    left = workArea.Right - width;
                    top = workArea.Top;
                    break;

                case WindowLocation.LeftCenter:
                    left = workArea.Left;
                    top = workArea.Top + (workArea.Height - height) / 2;
                    break;

                case WindowLocation.RightCenter:
                    left = workArea.Right - width;
                    top = workArea.Top + (workArea.Height - height) / 2;
                    break;

                case WindowLocation.BottomLeft:
                    left = workArea.Left;
                    top = workArea.Bottom - height;
                    break;

                case WindowLocation.BottomCenter:
                    left = workArea.Left + (workArea.Width - width) / 2;
                    top = workArea.Bottom - height;
                    break;

                case WindowLocation.BottomRight:
                    left = workArea.Right - width;
                    top = workArea.Bottom - height;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(location));
            }

            SetWindowPosFlags flags =
                SetWindowPosFlags.SWP_NOZORDER |
                SetWindowPosFlags.SWP_NOACTIVATE;
            if (!requestedSize.HasValue)
                flags |= SetWindowPosFlags.SWP_NOSIZE;

            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                left - leftInset,
                top - topInset,
                width + leftInset + rightInset,
                height + topInset + bottomInset,
                flags);
        }

        private static bool TryGetWindowFrameInsets(
            IntPtr hwnd,
            out RECT visibleBounds,
            out int leftInset,
            out int topInset,
            out int rightInset,
            out int bottomInset)
        {
            visibleBounds = default;
            leftInset = 0;
            topInset = 0;
            rightInset = 0;
            bottomInset = 0;

            if (!NativeMethods.GetWindowRect(hwnd, out RECT windowBounds) || windowBounds.IsEmpty)
                return false;

            if (!TryGetWindowBounds(hwnd, out visibleBounds))
                visibleBounds = windowBounds;

            leftInset = visibleBounds.Left - windowBounds.Left;
            topInset = visibleBounds.Top - windowBounds.Top;
            rightInset = windowBounds.Right - visibleBounds.Right;
            bottomInset = windowBounds.Bottom - visibleBounds.Bottom;
            return true;
        }

        /// <summary>
        /// 根据 HWND 查找当前应用中的 WPF <see cref="Window"/> 实例。
        /// </summary>
        /// <param name="hwnd">目标窗口句柄。</param>
        /// <returns>
        /// 若该句柄对应当前进程且属于当前 <see cref="Application"/> 的窗口实例，则返回对应窗口；
        /// 否则返回 <see langword="null"/>。
        /// </returns>
        public static Window? GetWindowFromHandle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || Application.Current == null)
                return null;

            foreach (Window window in Application.Current.Windows)
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == hwnd)
                    return window;
            }

            return null;
        }

        #region Style / ExStyle 修改

        /// <summary>
        /// 通知系统指定窗口的非客户区框架已发生变化，并触发布局/外框刷新。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        private static void ApplyFrameChanged(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return;

            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                SetWindowPosFlags.SWP_NOMOVE |
                SetWindowPosFlags.SWP_NOSIZE |
                SetWindowPosFlags.SWP_NOZORDER |
                SetWindowPosFlags.SWP_FRAMECHANGED);
        }

        /// <summary>
        /// 设置指定窗口样式位或扩展样式位。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="index">
        /// 样式索引，通常为 <c>GWL_STYLE</c> 或 <c>GWL_EXSTYLE</c>。
        /// </param>
        /// <param name="flag">要启用或禁用的位标志。</param>
        /// <param name="enable">
        /// 为 <see langword="true"/> 表示启用该位；
        /// 为 <see langword="false"/> 表示清除该位。
        /// </param>
        /// <remarks>
        /// <para>
        /// 仅当目标值实际发生变化时才写回。
        /// </para>
        /// <para>
        /// 对 <c>GWL_STYLE</c> 的修改会额外触发一次 <c>SWP_FRAMECHANGED</c>，
        /// 以便系统及时刷新标题栏、边框及系统按钮等非客户区外观。
        /// </para>
        /// <para>
        /// 对 <c>GWL_EXSTYLE</c> 的修改默认不触发框架刷新，以避免不必要的重绘和闪动。
        /// </para>
        /// </remarks>
        /// <returns>
        /// 若句柄有效且方法已完成处理，则返回 <see langword="true"/>；
        /// 若句柄无效则返回 <see langword="false"/>。
        /// </returns>
        private static bool SetLongFlag(IntPtr hwnd, int index, long flag, bool enable)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            long oldValue = NativeMethods.GetWindowLongPtr(hwnd, index).ToInt64();
            long newValue = enable
                ? (oldValue | flag)
                : (oldValue & ~flag);

            if (oldValue == newValue)
                return true;

            if (!TrySetWindowLongPtr(hwnd, index, new IntPtr(newValue)))
                return false;

            if (index == NativeMethods.GWL_STYLE)
                ApplyFrameChanged(hwnd);

            return true;
        }

        private static bool TrySetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        {
            Marshal.SetLastPInvokeError(0);
            IntPtr previous = NativeMethods.SetWindowLongPtr(hwnd, index, value);
            return previous != IntPtr.Zero || Marshal.GetLastPInvokeError() == 0;
        }

        /// <summary>
        /// 设置窗口样式位。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="flag">要设置或清除的样式位。</param>
        /// <param name="enable">
        /// 为 <see langword="true"/> 表示设置该位；
        /// 为 <see langword="false"/> 表示清除该位。
        /// </param>
        /// <returns>设置成功或目标样式无需修改时返回 <see langword="true"/>；句柄无效或底层调用失败时返回 <see langword="false"/>。</returns>
        public static bool SetStyleFlag(IntPtr hwnd, long flag, bool enable)
        {
            return SetLongFlag(hwnd, NativeMethods.GWL_STYLE, flag, enable);
        }

        /// <summary>
        /// 设置窗口扩展样式位。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="flag">要设置或清除的扩展样式位。</param>
        /// <param name="enable">
        /// 为 <see langword="true"/> 表示设置该位；
        /// 为 <see langword="false"/> 表示清除该位。
        /// </param>
        /// <returns>设置成功或目标样式无需修改时返回 <see langword="true"/>；句柄无效或底层调用失败时返回 <see langword="false"/>。</returns>
        public static bool SetExStyleFlag(IntPtr hwnd, long flag, bool enable)
        {
            return SetLongFlag(hwnd, NativeMethods.GWL_EXSTYLE, flag, enable);
        }

        /// <summary>
        /// 设置窗口无激活样式。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="enable">
        /// 为 <see langword="true"/> 表示设置为无激活窗口；
        /// 为 <see langword="false"/> 表示移除无激活样式。
        /// </param>
        public static void SetNoActivate(IntPtr hwnd, bool enable = true)
        {
            SetExStyleFlag(hwnd, WindowStyles.WS_EX_NOACTIVATE, enable);
        }

        /// <summary>
        /// 设置窗口 <c>WS_EX_TRANSPARENT</c> 扩展样式。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="enable">
        /// 为 <see langword="true"/> 表示设置该扩展样式；
        /// 为 <see langword="false"/> 表示移除该扩展样式。
        /// </param>
        /// <remarks>
        /// 该样式常与分层窗口配合用于实现穿透类效果，但并不等同于
        /// 基于 <c>WM_NCHITTEST</c> 返回 <c>HTTRANSPARENT</c> 的精确命中测试穿透。
        /// </remarks>
        public static void SetTransparent(IntPtr hwnd, bool enable = true)
        {
            SetExStyleFlag(hwnd, WindowStyles.WS_EX_TRANSPARENT, enable);
        }

        /// <summary>
        /// 设置工具窗口样式。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="enable">
        /// 为 <see langword="true"/> 表示设置为工具窗口；
        /// 为 <see langword="false"/> 表示移除工具窗口样式。
        /// </param>
        /// <remarks>
        /// 工具窗口通常不参与 Alt+Tab，且在任务栏中的显示行为也可能不同。
        /// </remarks>
        public static void SetToolWindow(IntPtr hwnd, bool enable = true)
        {
            SetExStyleFlag(hwnd, WindowStyles.WS_EX_TOOLWINDOW, enable);
        }

        /// <summary>
        /// 设置窗口鼠标穿透常用组合样式。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="enabled">
        /// 为 <see langword="true"/> 时设置 <c>WS_EX_TRANSPARENT | WS_EX_LAYERED</c>；
        /// 为 <see langword="false"/> 时移除该组合样式。
        /// </param>
        /// <remarks>
        /// <para>
        /// 启用后，窗口通常不再接收鼠标输入。
        /// </para>
        /// <para>
        /// 该方法适合叠加层、提示层、标注层等场景。
        /// </para>
        /// </remarks>
        public static void SetWindowMousePassthrough(IntPtr hwnd, bool enabled)
        {
            SetExStyleFlag(hwnd, WindowStyles.WS_EX_TRANSPARENT | WindowStyles.WS_EX_LAYERED, enabled);
        }

        /// <summary>
        /// 禁用窗口最大化能力。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <remarks>
        /// 该方法会移除 <c>WS_MAXIMIZEBOX</c>，
        /// 常用于工具窗口、悬浮窗等不希望进入最大化状态的场景。
        /// </remarks>
        public static void SetNoMaximize(IntPtr hwnd)
        {
            SetStyleFlag(hwnd, WindowStyles.WS_MAXIMIZEBOX, false);
        }

        #endregion

        #region Window text / class / process

        /// <summary>
        /// 获取窗口类名。失败时返回空字符串，不抛异常。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口类名；失败时返回空字符串。</returns>
        public static string GetClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            try
            {
                StringBuilder sb = new(256);
                int len = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                return len > 0 ? sb.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取窗口类名。该方法为 <see cref="GetClassName"/> 的别名。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口类名；失败时返回空字符串。</returns>
        public static string GetWindowClass(IntPtr hwnd)
        {
            return GetClassName(hwnd);
        }

        /// <summary>
        /// 若窗口当前处于最小化状态,则尝试将其还原。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// 窗口句柄无效时返回 <see langword="false"/>;
        /// 句柄有效时(无论本就未最小化,还是已派发还原命令)返回 <see langword="true"/>。
        /// </returns>
        /// <remarks>
        /// <c>ShowWindow</c> 的返回值代表窗口此前的可见状态,而非调用成功与否,
        /// 因此本方法在前置 <c>IsWindow</c> 通过后即视为"已尽力还原",不再以其返回值作为成败依据。
        /// </remarks>
        public static bool RestoreWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            if (!NativeMethods.IsIconic(hwnd))
                return true;

            NativeMethods.ShowWindow(hwnd, (int)ShowWindowCommands.Restore);
            return true;
        }

        /// <summary>
        /// 获取窗口标题。失败时返回空字符串，不抛异常。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口标题；失败时返回空字符串。</returns>
        public static string GetWindowText(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            try
            {
                int len = NativeMethods.GetWindowTextLength(hwnd);
                if (len <= 0)
                    return string.Empty;

                StringBuilder sb = new(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 获取窗口标题。该方法为 <see cref="GetWindowText"/> 的别名。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口标题；失败时返回空字符串。</returns>
        public static string GetWindowTitle(IntPtr hwnd)
        {
            return GetWindowText(hwnd);
        }

        /// <summary>
        /// 获取窗口所属进程 ID。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>成功时返回进程 ID；失败时返回 0。</returns>
        public static int GetWindowProcessId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return 0;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return unchecked((int)pid);
        }

        /// <summary>
        /// 根据进程 ID 获取可执行文件完整路径。失败时返回空字符串，不抛异常。
        /// </summary>
        /// <param name="pid">进程 ID。</param>
        /// <returns>成功时返回主模块完整路径；失败时返回空字符串。</returns>
        public static string GetProcessFilePath(int pid)
        {
            try
            {
                if (pid <= 0)
                    return string.Empty;

                using (var process = Process.GetProcessById(pid))
                {
                    return process.MainModule != null
                        ? process.MainModule.FileName ?? string.Empty
                        : string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 判断指定窗口是否为桌面相关窗口（桌面窗口本身、Shell 窗口及其子窗口，
        /// 或 Progman/WorkerW 等已知桌面承载窗口）。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// <see langword="true"/> 表示该窗口被判定为桌面相关窗口；否则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// 该方法基于桌面窗口、Shell 窗口及若干已知桌面承载窗口类名（如 <c>Progman</c>、<c>WorkerW</c>、
        /// <c>SHELLDLL_DefView</c>、<c>SysListView32</c>）进行启发式判断，可能因系统版本差异
        /// 或创建同名窗口类的第三方程序（如壁纸/叠加层工具）而产生误判。
        /// </remarks>
        public static bool IsDesktopWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            IntPtr desktopWindow = NativeMethods.GetDesktopWindow();
            if (desktopWindow != IntPtr.Zero && hwnd == desktopWindow)
                return true;

            IntPtr shellWindow = NativeMethods.GetShellWindow();

            if (hwnd == shellWindow)
                return true;

            if (shellWindow != IntPtr.Zero && NativeMethods.IsChild(shellWindow, hwnd))
                return true;

            IntPtr root = WindowHelper.GetRootWindow(hwnd);

            string className = WindowHelper.GetClassName(root);

            return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取指定窗口的根窗口句柄。
        /// </summary>
        /// <param name="hwnd">起始窗口句柄。</param>
        /// <returns>
        /// 该窗口的根窗口句柄；若查询失败，或根窗口即为桌面窗口本身，则返回原始 <paramref name="hwnd"/>；
        /// <paramref name="hwnd"/> 为 <see cref="IntPtr.Zero"/> 时返回 <see cref="IntPtr.Zero"/>。
        /// </returns>
        public static IntPtr GetRootWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr desktopWindow = NativeMethods.GetDesktopWindow();
            IntPtr ancestor = NativeMethods.GetAncestor(hwnd, GetAncestorFlags.GA_ROOT);

            if (ancestor == IntPtr.Zero || ancestor == desktopWindow)
                return hwnd;

            return ancestor;
        }

        #endregion

        #region 鼠标位置 / 显示器信息（物理像素）

        /// <summary>
        /// 获取当前鼠标位置（屏幕坐标，物理像素）。
        /// </summary>
        /// <param name="pt">输出的鼠标坐标。</param>
        /// <returns>成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryGetMousePosition(out POINT pt)
        {
            return NativeMethods.GetCursorPos(out pt);
        }

        /// <summary>
        /// 获取当前鼠标位置（屏幕坐标，物理像素）。
        /// </summary>
        /// <returns>鼠标坐标。若底层调用失败，则返回默认值。</returns>
        public static POINT GetMousePosition()
        {
            POINT pt;
            NativeMethods.GetCursorPos(out pt);
            return pt;
        }

        /// <summary>
        /// 获取当前鼠标位置（屏幕坐标，物理像素），优先使用物理坐标接口。
        /// </summary>
        /// <returns>鼠标物理坐标。</returns>
        public static POINT GetMousePhysicalPosition()
        {
            POINT pt;

            if (NativeMethods.GetPhysicalCursorPos(out pt))
                return pt;

            NativeMethods.GetCursorPos(out pt);
            return pt;
        }

        /// <summary>
        /// 根据屏幕坐标点获取其所在显示器的屏幕区域与工作区。
        /// </summary>
        /// <param name="x">屏幕坐标 X（物理像素）。</param>
        /// <param name="y">屏幕坐标 Y（物理像素）。</param>
        /// <param name="monitorRect">输出的显示器完整区域。</param>
        /// <param name="workRect">输出的显示器工作区。</param>
        /// <returns>成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryGetMonitorBoundsFromPoint(int x, int y, out RECT monitorRect, out RECT workRect)
        {
            monitorRect = default(RECT);
            workRect = default(RECT);

            IntPtr hMon = NativeMethods.MonitorFromPoint(
                new POINT { X = x, Y = y },
                NativeMethods.MONITOR_DEFAULTTONEAREST);

            if (hMon == IntPtr.Zero)
                return false;

            var mi = new MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO))
            };

            if (!NativeMethods.GetMonitorInfo(hMon, ref mi))
                return false;

            monitorRect = mi.rcMonitor;
            workRect = mi.rcWork;
            return true;
        }

        /// <summary>
        /// 获取指定窗口所在显示器的完整区域。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// 成功时返回所在显示器的区域；若窗口边界获取失败或显示器定位失败，则退回返回窗口自身边界
        /// （句柄无效时为默认 <see cref="RECT"/>）。
        /// </returns>
        /// <remarks>
        /// 该方法优先以窗口中心点所落入的显示器作为归属显示器。
        /// </remarks>
        public static RECT GetMonitorBoundsFromWindow(IntPtr hwnd)
        {
            RECT bounds;
            if (!TryGetWindowBounds(hwnd, out bounds))
                return bounds;

            int cx = bounds.Left + (bounds.Width / 2);
            int cy = bounds.Top + (bounds.Height / 2);

            RECT monitorRect;
            RECT workRect;
            if (TryGetMonitorBoundsFromPoint(cx, cy, out monitorRect, out workRect))
                return monitorRect;

            return bounds;
        }

        #endregion

        #region 窗口边界（物理像素）

        /// <summary>
        /// 获取窗口真实边界。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="rect">输出的窗口边界。</param>
        /// <returns>成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        /// <remarks>
        /// <para>
        /// 优先使用 DWM 的扩展框架边界，以获得更贴近视觉外框的结果。
        /// </para>
        /// <para>
        /// 若 DWM 调用失败，则回退到 <c>GetWindowRect</c>。
        /// </para>
        /// </remarks>
        public static bool TryGetWindowBounds(IntPtr hwnd, out RECT rect)
        {
            rect = default(RECT);

            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            try
            {
                int hr = NativeMethods.DwmGetWindowAttribute(
                    hwnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rect,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(RECT)));

                if (hr == 0 && !rect.IsEmpty)
                    return true;
            }
            catch
            {
            }

            return NativeMethods.GetWindowRect(hwnd, out rect) && !rect.IsEmpty;
        }

        /// <summary>
        /// 获取窗口真实边界。失败时返回默认 <see cref="RECT"/>。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口边界；失败时返回默认值。</returns>
        public static RECT GetWindowBounds(IntPtr hwnd)
        {
            RECT rect;
            TryGetWindowBounds(hwnd, out rect);
            return rect;
        }

        /// <summary>
        /// 获取窗口矩形。该方法直接调用 Win32 <c>GetWindowRect</c>。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>窗口矩形；失败时返回默认值。</returns>
        public static RECT GetWindowRect(IntPtr hwnd)
        {
            RECT rect;
            NativeMethods.GetWindowRect(hwnd, out rect);
            return rect;
        }

        #endregion

        #region  Taskbar

        /// <summary>
        /// 判断指定窗口当前是否显示在任务栏中。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// <see langword="true"/> 表示当前按常见规则应显示在任务栏中;
        /// 否则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// 该方法基于可见性、扩展样式与 Owner 关系进行推断,结果用于常见窗口判断场景。
        /// 不可见窗口直接视为不在任务栏中。
        /// </remarks>
        public static bool IsShownOnTaskbar(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            if (!NativeMethods.IsWindowVisible(hwnd))
                return false;

            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();

            bool noToolWindow = (style & WindowStyles.WS_EX_TOOLWINDOW) == 0;
            bool hasAppWindow = (style & WindowStyles.WS_EX_APPWINDOW) != 0;
            bool noOwner = NativeMethods.GetWindow(hwnd, GetWindowType.GW_OWNER) == IntPtr.Zero;

            return (noToolWindow && noOwner) || hasAppWindow;
        }

        #endregion

        #region TopMost / Restore / Foreground

        /// <summary>
        /// 设置指定窗口是否置顶。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <param name="topMost">是否置顶。</param>
        /// <param name="noActivate">是否在本次设置过程中避免激活窗口。</param>
        /// <param name="showIfHidden">若窗口当前隐藏，是否在设置置顶时一并显示。</param>
        /// <returns>设置成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool SetTopMost(IntPtr hwnd, bool topMost = true, bool noActivate = false, bool showIfHidden = false)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            IntPtr insertAfter = topMost
                ? NativeMethods.HWND_TOPMOST
                : NativeMethods.HWND_NOTOPMOST;

            SetWindowPosFlags flags = SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE;

            if (noActivate)
                flags |= SetWindowPosFlags.SWP_NOACTIVATE;

            if (showIfHidden)
                flags |= SetWindowPosFlags.SWP_SHOWWINDOW;

            return NativeMethods.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, flags);
        }

        /// <summary>
        /// 若窗口已最小化则先还原，然后尝试将其置于前台。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// 操作成功时返回 <see langword="true"/>；若句柄无效或前台切换失败则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// <para>
        /// <c>SetForegroundWindow</c> 受系统前台激活策略限制，即使窗口有效也可能失败。
        /// </para>
        /// <para>
        /// 本方法仅做一次尽力尝试，不保证在所有前台限制场景下成功抢占焦点。
        /// </para>
        /// </remarks>
        public static bool RestoreAndSetForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
                return false;

            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, (int)ShowWindowCommands.Restore);

            return NativeMethods.SetForegroundWindow(hwnd);
        }

        public static bool SetForegroundWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero &&
                   NativeMethods.IsWindow(hwnd) &&
                   !NativeMethods.IsIconic(hwnd) &&
                   NativeMethods.IsWindowVisible(hwnd) &&
                   NativeMethods.SetForegroundWindow(hwnd);
        }

        #endregion

        #region 工具窗口判断 / 枚举可见窗口

        /// <summary>
        /// 判断指定窗口是否为工具窗口。
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// <see langword="true"/> 表示扩展样式中包含 <c>WS_EX_TOOLWINDOW</c>；
        /// 否则返回 <see langword="false"/>。
        /// </returns>
        public static bool IsToolWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            return (ex & WindowStyles.WS_EX_TOOLWINDOW) != 0;
        }

        /// <summary>
        /// 枚举当前桌面上的可见顶层窗口，并返回其边界集合。
        /// </summary>
        /// <param name="excludeHwnd">要排除的窗口句柄，通常传当前窗口自身句柄。</param>
        /// <param name="onError">枚举某个单独窗口时发生异常的回调，可为 <see langword="null"/>。</param>
        /// <returns>以 HWND 为键、窗口边界为值的字典。</returns>
        /// <remarks>
        /// 默认会排除：
        /// <list type="bullet">
        /// <item><description>无效句柄</description></item>
        /// <item><description>指定排除句柄</description></item>
        /// <item><description>不可见窗口</description></item>
        /// <item><description>最小化窗口</description></item>
        /// <item><description>工具窗口</description></item>
        /// <item><description>无法获取有效边界或边界为空的窗口</description></item>
        /// </list>
        /// </remarks>
        public static Dictionary<IntPtr, RECT> GetVisibleWindowRects(IntPtr excludeHwnd = default(IntPtr), Action<Exception>? onError = null)
        {
            Dictionary<IntPtr, RECT> result = [];

            bool ok = NativeMethods.EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (hwnd == IntPtr.Zero)
                        return true;

                    if (!NativeMethods.IsWindow(hwnd))
                        return true;

                    if (excludeHwnd != IntPtr.Zero && hwnd == excludeHwnd)
                        return true;

                    if (!NativeMethods.IsWindowVisible(hwnd))
                        return true;

                    if (NativeMethods.IsIconic(hwnd))
                        return true;

                    if (IsToolWindow(hwnd))
                        return true;

                    RECT rect;
                    if (!TryGetWindowBounds(hwnd, out rect))
                        return true;

                    if (rect.IsEmpty)
                        return true;

                    result[hwnd] = rect;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (onError != null)
                    {
                        // 防止用户回调抛出的异常穿透 native EnumWindows 回调,
                        // 中断枚举并让"安全枚举"语义失效。
                        try { onError(ex); }
                        catch (Exception cbEx) when (cbEx is not OutOfMemoryException)
                        {
                            Debug.WriteLine("GetVisibleWindowRects: onError callback failed: " + cbEx);
                        }
                    }
                }

                return true;
            }, IntPtr.Zero);

            if (!ok && onError != null)
            {
                try { onError(new Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed.")); }
                catch (Exception cbEx) when (cbEx is not OutOfMemoryException)
                {
                    Debug.WriteLine("GetVisibleWindowRects: onError callback failed: " + cbEx);
                }
            }

            return result;
        }

        #endregion
    }
}
