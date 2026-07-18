using System;

namespace WidgetCanvas.Infrastructure.Hooks
{
    /// <summary>
    /// 全局鼠标 Hook 事件参数。
    /// </summary>
    public sealed class MouseHookEventArgs : EventArgs
    {
        // MSLLHOOKSTRUCT.mouseData 的高位用途：
        //   WM_MOUSEWHEEL / WM_MOUSEHWHEEL → 有符号滚轮增量（WHEEL_DELTA = 120 的倍数）
        //   WM_XBUTTON*                    → XButton 编号（1 = XBUTTON1, 2 = XBUTTON2）
        private const uint XBUTTON1 = 0x0001;
        private const uint XBUTTON2 = 0x0002;
        private const uint LLMHF_INJECTED = 0x0001;
        private const uint LLMHF_LOWER_IL_INJECTED = 0x0002;

        /// <summary>
        /// 初始化一个鼠标 Hook 事件参数实例。
        /// </summary>
        /// <param name="message">鼠标消息类型。</param>
        /// <param name="x">屏幕物理像素 X 坐标。</param>
        /// <param name="y">屏幕物理像素 Y 坐标。</param>
        /// <param name="mouseData">原始鼠标附加数据（MSLLHOOKSTRUCT.mouseData）。</param>
        internal MouseHookEventArgs(
            MouseMessage message,
            int x,
            int y,
            uint mouseData,
            uint flags,
            uint time,
            IntPtr extraInfo)
        {
            Message = message;
            X = x;
            Y = y;
            RawMouseData = mouseData;
            Flags = flags;
            Time = time;
            ExtraInfo = extraInfo;

            uint highWord = mouseData >> 16;

            WheelDelta = (message == MouseMessage.MouseWheel
                                 || message == MouseMessage.MouseHWheel)
                                 ? unchecked((short)highWord)
                                 : 0;
            IsHorizontalWheel = message == MouseMessage.MouseHWheel;
            XButton = (message == MouseMessage.XButtonDown
                                 || message == MouseMessage.XButtonUp
                                 || message == MouseMessage.XButtonDoubleClick)
                                 ? (highWord == XBUTTON2 ? 2u : 1u)
                                 : 0u;
        }

        /// <summary>获取鼠标消息类型。</summary>
        public MouseMessage Message { get; }

        /// <summary>获取屏幕物理像素 X 坐标。</summary>
        public int X { get; }

        /// <summary>获取屏幕物理像素 Y 坐标。</summary>
        public int Y { get; }

        /// <summary>
        /// 获取滚轮增量。
        /// </summary>
        /// <remarks>
        /// 仅在 <see cref="MouseMessage.MouseWheel"/> 或
        /// <see cref="MouseMessage.MouseHWheel"/> 消息中有意义，其他消息为 0。
        /// 正值表示向前/向右滚动，负值表示向后/向左滚动。
        /// </remarks>
        public int WheelDelta { get; }

        /// <summary>
        /// 获取当前事件是否为横向滚轮。
        /// </summary>
        public bool IsHorizontalWheel { get; }

        /// <summary>
        /// 获取触发事件的 XButton 编号。
        /// </summary>
        /// <remarks>
        /// 仅在 <see cref="MouseMessage.XButtonDown"/>、<see cref="MouseMessage.XButtonUp"/>、
        /// <see cref="MouseMessage.XButtonDoubleClick"/> 消息中有意义。
        /// <c>1</c> 表示 XBUTTON1（通常为后退键），<c>2</c> 表示 XBUTTON2（通常为前进键），
        /// 其他消息为 <c>0</c>。
        /// </remarks>
        public uint XButton { get; }

        /// <summary>
        /// 获取原始鼠标附加数据。
        /// </summary>
        public uint RawMouseData { get; }

        /// <summary>
        /// 获取低级鼠标 Hook 标志位。
        /// </summary>
        public uint Flags { get; }

        /// <summary>
        /// 获取事件时间戳。
        /// </summary>
        public uint Time { get; }

        /// <summary>
        /// 获取事件附加信息。
        /// </summary>
        public IntPtr ExtraInfo { get; }

        /// <summary>
        /// 获取当前鼠标事件是否由注入输入产生。
        /// </summary>
        public bool IsInjected => (Flags & LLMHF_INJECTED) != 0;

        /// <summary>
        /// 获取当前鼠标事件是否由低完整性级别进程注入产生。
        /// </summary>
        public bool IsLowerIntegrityInjected => (Flags & LLMHF_LOWER_IL_INJECTED) != 0;

        /// <summary>
        /// 获取或设置是否拦截当前鼠标消息。
        /// </summary>
        /// <remarks>
        /// 当设置为 <c>true</c> 时，当前消息不会继续传递给后续 Hook 或目标窗口。
        /// </remarks>
        public bool Handled { get; set; }

        /// <summary>
        /// 返回当前事件参数的调试字符串。
        /// </summary>
        public override string ToString()
        {
            return string.Format(
                "{0} ({1},{2}) Δ={3} Injected={4} Handled={5}",
                Message,
                X,
                Y,
                WheelDelta,
                IsInjected,
                Handled);
        }
    }

    /// <summary>
    /// 低级鼠标消息类型。
    /// </summary>
    public enum MouseMessage
    {
        /// <summary>鼠标移动。</summary>
        MouseMove = 0x0200,
        /// <summary>左键按下。</summary>
        LButtonDown = 0x0201,
        /// <summary>左键抬起。</summary>
        LButtonUp = 0x0202,
        /// <summary>左键双击。</summary>
        LButtonDoubleClick = 0x0203,
        /// <summary>右键按下。</summary>
        RButtonDown = 0x0204,
        /// <summary>右键抬起。</summary>
        RButtonUp = 0x0205,
        /// <summary>右键双击。</summary>
        RButtonDoubleClick = 0x0206,   // WM_RBUTTONDBLCLK，位于 RButtonUp 和 MButtonDown 之间
        /// <summary>中键按下。</summary>
        MButtonDown = 0x0207,
        /// <summary>中键抬起。</summary>
        MButtonUp = 0x0208,
        /// <summary>中键双击。</summary>
        MButtonDoubleClick = 0x0209,   // WM_MBUTTONDBLCLK，位于 MButtonUp 和 MouseWheel 之间
        /// <summary>纵向滚轮。</summary>
        MouseWheel = 0x020A,
        /// <summary>扩展鼠标键按下（XBUTTON1 / XBUTTON2）。</summary>
        XButtonDown = 0x020B,
        /// <summary>扩展鼠标键抬起（XBUTTON1 / XBUTTON2）。</summary>
        XButtonUp = 0x020C,
        /// <summary>扩展鼠标键双击（XBUTTON1 / XBUTTON2）。</summary>
        XButtonDoubleClick = 0x020D,
        /// <summary>横向滚轮。</summary>
        MouseHWheel = 0x020E
    }
}
