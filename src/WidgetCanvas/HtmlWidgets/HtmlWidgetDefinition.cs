#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WidgetCanvas.HtmlWidgets
{
    internal enum HtmlWidgetHome
    {
        Canvas,
        Library
    }

    /// <summary>
    /// 描述浮岛画布中的一个 HTML 组件。
    /// </summary>
    internal sealed class HtmlWidgetDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Html { get; set; } = string.Empty;

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; } = 320;

        public double Height { get; set; } = 220;

        public bool IsLocked { get; set; }

        /// <summary>
        /// 独立窗口关闭后的归处。独立窗口是否正在运行由宿主运行时状态判断。
        /// 这样即使宿主异常退出，组件下次仍会回到原来的浮岛或组件库。
        /// </summary>
        public HtmlWidgetHome Home { get; set; } = HtmlWidgetHome.Canvas;

        /// <summary>
        /// 独立组件窗口最后一次关闭或隐藏时的物理像素边界。
        /// </summary>
        public string DetachedPosition { get; set; } = string.Empty;

        /// <summary>
        /// 独立组件窗口是否使用 WPF 置顶。不会执行原生 Z-order 调整。
        /// </summary>
        public bool DetachedTopmost { get; set; } = true;

        /// <summary>
        /// 独立组件窗口是否启用上、左、右屏幕边缘的贴边自动隐藏。
        /// </summary>
        public bool DetachedAutoHide { get; set; }

        /// <summary>
        /// 由组件内 <c>window.widgetHost.state</c> API 保存的实例级状态。
        /// </summary>
        public Dictionary<string, JsonElement> State { get; set; } = new(StringComparer.Ordinal);
    }
}
