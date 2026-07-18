using WidgetCanvas.Infrastructure.Win32;
using Microsoft.Win32.SafeHandles;
using System;

namespace WidgetCanvas.Infrastructure.Hooks.Interop
{
    /// <summary>
    /// Win32 Hook 句柄的安全封装。
    /// </summary>
    /// <remarks>
    /// 在释放时自动调用 <c>UnhookWindowsHookEx</c>。
    /// </remarks>
    internal sealed class SafeHookHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// 使用指定原生句柄初始化安全句柄实例。
        /// </summary>
        /// <param name="handle">原生 Hook 句柄。</param>
        public SafeHookHandle(IntPtr handle)
            : base(true)
        {
            SetHandle(handle);
        }

        /// <summary>
        /// 释放底层原生句柄。
        /// </summary>
        /// <returns>释放是否成功。</returns>
        protected override bool ReleaseHandle()
        {
            return NativeMethods.UnhookWindowsHookEx(handle);
        }
    }
}