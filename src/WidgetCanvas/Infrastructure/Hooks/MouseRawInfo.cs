namespace WidgetCanvas.Infrastructure.Hooks
{
    /// <summary>
    /// 鼠标原始轻量数据。
    /// </summary>
    /// <remarks>
    /// 用于 <see cref="GlobalMouseHook.MouseRawEvent"/> 高频场景，
    /// 仅包含消息类型与屏幕物理像素坐标，无堆分配。
    /// </remarks>
    public readonly struct MouseRawInfo
    {
        /// <summary>
        /// 初始化一个鼠标原始轻量数据实例。
        /// </summary>
        /// <param name="message">鼠标消息类型。</param>
        /// <param name="x">屏幕物理像素 X 坐标。</param>
        /// <param name="y">屏幕物理像素 Y 坐标。</param>
        public MouseRawInfo(MouseMessage message, int x, int y)
        {
            Message = message;
            X = x;
            Y = y;
        }

        /// <summary>获取鼠标消息类型。</summary>
        public MouseMessage Message { get; }

        /// <summary>获取屏幕物理像素 X 坐标。</summary>
        public int X { get; }

        /// <summary>获取屏幕物理像素 Y 坐标。</summary>
        public int Y { get; }

        /// <summary>
        /// 返回当前数据的调试字符串。
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} ({1},{2})", Message, X, Y);
        }
    }
}
