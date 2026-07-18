using WidgetCanvas.Infrastructure.Hooks.Interop;
using System;
using System.Runtime.InteropServices;

namespace WidgetCanvas.Infrastructure.Hooks
{
    /// <summary>
    /// 全局低级鼠标 Hook。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该类基于 <c>WH_MOUSE_LL</c>，可监听系统范围内的鼠标输入。
    /// </para>
    /// <para>
    /// 提供两个事件：
    /// <list type="bullet">
    /// <item>
    ///   <description>
    ///     <see cref="MouseRawEvent"/>：轻量级，无堆分配，适合高频 Move 场景；
    ///     不支持拦截消息。
    ///   </description>
    /// </item>
    /// <item>
    ///   <description>
    ///     <see cref="MouseEvent"/>：完整事件参数，支持通过
    ///     <see cref="MouseHookEventArgs.Handled"/> 拦截消息。
    ///   </description>
    /// </item>
    /// </list>
    /// 两个事件在同一次回调中均会触发，<see cref="MouseRawEvent"/> 先行。
    /// </para>
    /// </remarks>
    public sealed class GlobalMouseHook : LowLevelGlobalHookBase
    {
        /// <summary>
        /// 获取当前 Hook 类型。
        /// </summary>
        protected override int HookType
        {
            get => NativeConstants.WH_MOUSE_LL;
        }

        /// <summary>
        /// 当收到任意鼠标消息时发生（轻量级，无堆分配）。
        /// 仅包含消息类型与坐标，不支持拦截。
        /// 适合高频追踪场景；如需拦截或完整数据，请使用 <see cref="MouseEvent"/>。
        /// </summary>
        public event Action<MouseRawInfo>? MouseRawEvent;

        /// <summary>
        /// 当收到鼠标消息时发生（完整事件参数）。
        /// </summary>
        /// <remarks>
        /// 事件处理程序可通过设置 <see cref="MouseHookEventArgs.Handled"/> 为 <c>true</c>
        /// 来拦截当前鼠标消息，使其不再传递给后续 Hook 或目标窗口。
        /// </remarks>
        public event EventHandler<MouseHookEventArgs>? MouseEvent;

        /// <summary>
        /// 初始化一个全局低级鼠标 Hook。
        /// </summary>
        public GlobalMouseHook()
        {
            HookProcInstance = Callback;
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (ShouldHandleCallback(code))
            {
                MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseMessage message = (MouseMessage)(int)wParam;

                // 轻量事件：先触发，无分配
                Action<MouseRawInfo>? rawHandler = MouseRawEvent;
                if (rawHandler != null)
                {
                    try
                    {
                        rawHandler(new MouseRawInfo(message, data.pt.x, data.pt.y));
                    }
                    catch (Exception ex)
                    {
                        TraceEventHandlerException(nameof(MouseRawEvent), ex);
                    }
                }

                // 完整事件：支持拦截
                EventHandler<MouseHookEventArgs>? handler = MouseEvent;
                if (handler != null)
                {
                    MouseHookEventArgs args = new MouseHookEventArgs(
                        message,
                        data.pt.x,
                        data.pt.y,
                        data.mouseData,
                        data.flags,
                        data.time,
                        data.dwExtraInfo);
                    try
                    {
                        handler(this, args);
                    }
                    catch (Exception ex)
                    {
                        TraceEventHandlerException(nameof(MouseEvent), ex);
                    }

                    if (args.Handled)
                        return HandledHookResult;
                }
            }

            return CallNextHook(code, wParam, lParam);
        }
    }
}
