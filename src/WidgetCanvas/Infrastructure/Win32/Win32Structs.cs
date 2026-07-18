using System;
using System.Runtime.InteropServices;

namespace WidgetCanvas.Infrastructure.Win32
{
    /// <summary>
    /// Win32 点坐标结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        /// <summary>
        /// X 坐标。
        /// </summary>
        public int X;

        /// <summary>
        /// Y 坐标。
        /// </summary>
        public int Y;

        /// <summary>
        /// 返回当前点的文本表示。
        /// </summary>
        /// <returns>形如 <c>X=10, Y=20</c> 的字符串。</returns>
        public override string ToString()
        {
            return "X=" + X + ", Y=" + Y;
        }
    }

    /// <summary>
    /// Win32 矩形结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        /// <summary>
        /// 左边界。
        /// </summary>
        public int Left;

        /// <summary>
        /// 上边界。
        /// </summary>
        public int Top;

        /// <summary>
        /// 右边界。
        /// </summary>
        public int Right;

        /// <summary>
        /// 下边界。
        /// </summary>
        public int Bottom;

        /// <summary>
        /// 获取矩形宽度，等于 <c>Right - Left</c>。
        /// </summary>
        public int Width
        {
            get => Right - Left;
        }

        /// <summary>
        /// 获取矩形高度，等于 <c>Bottom - Top</c>。
        /// </summary>
        public int Height
        {
            get => Bottom - Top;
        }

        /// <summary>
        /// 获取当前矩形是否为空。
        /// </summary>
        /// <remarks>
        /// 当宽度小于等于 0 或高度小于等于 0 时，视为空矩形。
        /// </remarks>
        public bool IsEmpty
        {
            get => Width <= 0 || Height <= 0;
        }

        /// <summary>
        /// 判断当前矩形是否包含指定点。
        /// </summary>
        /// <param name="x">点的 X 坐标。</param>
        /// <param name="y">点的 Y 坐标。</param>
        /// <returns>包含返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        /// <remarks>
        /// 左、上边界为包含；右、下边界为不包含。
        /// </remarks>
        public bool Contains(int x, int y)
        {
            return x >= Left && x < Right && y >= Top && y < Bottom;
        }

        /// <summary>
        /// 判断当前矩形是否包含指定点。
        /// </summary>
        /// <param name="point">待判断的点。</param>
        /// <returns>包含返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool Contains(POINT point)
        {
            return Contains(point.X, point.Y);
        }

        /// <summary>
        /// 判断当前矩形在附加边距后是否包含指定点。
        /// </summary>
        /// <param name="x">点的 X 坐标。</param>
        /// <param name="y">点的 Y 坐标。</param>
        /// <param name="margin">向四周扩展的边距值。可为负数。</param>
        /// <returns>包含返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        /// <remarks>
        /// 左、上边界为包含；右、下边界为不包含。
        /// </remarks>
        public bool ContainsWithMargin(int x, int y, int margin)
        {
            long left = (long)Left - margin;
            long top = (long)Top - margin;
            long right = (long)Right + margin;
            long bottom = (long)Bottom + margin;

            return x >= left && x < right && y >= top && y < bottom;
        }

        /// <summary>
        /// 判断当前矩形是否与另一矩形相交。
        /// </summary>
        /// <param name="other">另一矩形。</param>
        /// <returns>相交返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool IntersectsWith(RECT other)
        {
            return !(other.Right <= Left || other.Left >= Right || other.Bottom <= Top || other.Top >= Bottom);
        }

        /// <summary>
        /// 按指定偏移量移动矩形。
        /// </summary>
        /// <param name="dx">X 方向偏移量。</param>
        /// <param name="dy">Y 方向偏移量。</param>
        public void Offset(int dx, int dy)
        {
            Left += dx;
            Top += dy;
            Right += dx;
            Bottom += dy;
        }

        /// <summary>
        /// 按指定值向四周扩张或收缩矩形。
        /// </summary>
        /// <param name="dx">水平方向扩张值。正数表示向左右扩张，负数表示收缩。</param>
        /// <param name="dy">垂直方向扩张值。正数表示向上下扩张，负数表示收缩。</param>
        public void Inflate(int dx, int dy)
        {
            Left -= dx;
            Top -= dy;
            Right += dx;
            Bottom += dy;
        }

        /// <summary>
        /// 返回当前矩形的文本表示。
        /// </summary>
        /// <returns>形如 <c>Left,Top,Width,Height</c> 的字符串。</returns>
        public override string ToString()
        {
            return $"{Left},{Top},{Width},{Height}";
        }
    }

    /// <summary>
    /// 显示器信息结构。
    /// </summary>
    /// <remarks>
    /// 调用 <c>GetMonitorInfo</c> 前，必须先为 <see cref="cbSize"/> 赋值。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        /// <summary>
        /// 结构体大小。调用相关 Win32 API 前必须正确赋值。
        /// </summary>
        public int cbSize;

        /// <summary>
        /// 显示器区域（通常为屏幕坐标系中的完整显示区域）。
        /// </summary>
        public RECT rcMonitor;

        /// <summary>
        /// 工作区区域（扣除任务栏等保留区域后的可用区域）。
        /// </summary>
        public RECT rcWork;

        /// <summary>
        /// 显示器标志。
        /// </summary>
        public uint dwFlags;

        /// <summary>
        /// 创建一个已正确初始化 <see cref="cbSize"/> 的 <see cref="MONITORINFO"/> 实例。
        /// </summary>
        /// <returns>可直接传给 Win32 API 使用的 <see cref="MONITORINFO"/> 实例。</returns>
        public static MONITORINFO Create()
        {
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            return info;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }
}
