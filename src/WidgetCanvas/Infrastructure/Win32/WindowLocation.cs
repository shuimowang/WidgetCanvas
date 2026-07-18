namespace WidgetCanvas.Infrastructure.Win32
{
    /// <summary>
    /// 窗口在鼠标所在屏幕中的定位方式。
    /// </summary>
    public enum WindowLocation
    {
        /// <summary>
        /// 有四值物理像素矩形时恢复完整边界，否则在工作区中央显示。
        /// </summary>
        Auto = 0,

        /// <summary>
        /// 工作区中央。
        /// </summary>
        Center,

        /// <summary>
        /// 鼠标附近，并自动避开工作区边缘。
        /// </summary>
        NearMouse,

        /// <summary>
        /// 工作区左上角。
        /// </summary>
        TopLeft,

        /// <summary>
        /// 工作区顶部中央。
        /// </summary>
        TopCenter,

        /// <summary>
        /// 工作区右上角。
        /// </summary>
        TopRight,

        /// <summary>
        /// 工作区左侧中央。
        /// </summary>
        LeftCenter,

        /// <summary>
        /// 工作区右侧中央。
        /// </summary>
        RightCenter,

        /// <summary>
        /// 工作区左下角。
        /// </summary>
        BottomLeft,

        /// <summary>
        /// 工作区底部中央。
        /// </summary>
        BottomCenter,

        /// <summary>
        /// 工作区右下角。
        /// </summary>
        BottomRight,

        /// <summary>
        /// 覆盖鼠标当前所在显示器的完整边界，包括任务栏所在区域。
        /// </summary>
        FullScreen
    }
}
