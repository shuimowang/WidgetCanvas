// ==============================
// File: WindowStyles.cs
// ==============================
namespace WidgetCanvas.Infrastructure.Win32
{
    /// <summary>
    /// 窗口样式（GWL_STYLE）与扩展样式（GWL_EXSTYLE）常量。
    /// 用法：
    /// style |= flag  开启
    /// style &= ~flag 关闭
    /// </summary>
    public static class WindowStyles
    {
        #region GWL_STYLE

        public const int WS_OVERLAPPED = 0x00000000;                    // 重叠窗口基础样式
        public const int WS_POPUP = unchecked((int)0x80000000);        // 弹出窗口
        public const int WS_CHILD = 0x40000000;                        // 子窗口
        public const int WS_MINIMIZE = 0x20000000;                     // 当前为最小化状态
        public const int WS_VISIBLE = 0x10000000;                      // 当前可见
        public const int WS_DISABLED = 0x08000000;                     // 当前禁用
        public const int WS_CLIPSIBLINGS = 0x04000000;                 // 裁剪同级窗口重叠区域
        public const int WS_CLIPCHILDREN = 0x02000000;                 // 裁剪子窗口区域
        public const int WS_MAXIMIZE = 0x01000000;                     // 当前为最大化状态
        public const int WS_CAPTION = 0x00C00000;                      // 标题栏（含边框）
        public const int WS_BORDER = 0x00800000;                       // 单线边框
        public const int WS_DLGFRAME = 0x00400000;                     // 对话框边框
        public const int WS_VSCROLL = 0x00200000;                      // 垂直滚动条
        public const int WS_HSCROLL = 0x00100000;                      // 水平滚动条
        public const int WS_SYSMENU = 0x00080000;                      // 系统菜单
        public const int WS_THICKFRAME = 0x00040000;                   // 可调大小边框

        public const int WS_MINIMIZEBOX = 0x00020000;                  // 最小化按钮
        public const int WS_MAXIMIZEBOX = 0x00010000;                  // 最大化按钮

        public const int WS_OVERLAPPEDWINDOW =
            WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU |
            WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;           // 标准重叠窗口样式组合

        public const int WS_POPUPWINDOW =
            WS_POPUP | WS_BORDER | WS_SYSMENU;                         // 标准弹出窗口样式组合

        #endregion GWL_STYLE

        #region GWL_EXSTYLE

        public const int WS_EX_DLGMODALFRAME = 0x00000001;             // 双边框对话框样式
        public const int WS_EX_NOPARENTNOTIFY = 0x00000004;            // 子窗口创建/销毁时不通知父窗口
        public const int WS_EX_TOPMOST = 0x00000008;                   // 置顶窗口
        public const int WS_EX_ACCEPTFILES = 0x00000010;               // 接收拖放文件
        public const int WS_EX_TRANSPARENT = 0x00000020;               // 常用于窗口穿透类效果，但不同于基于 WM_NCHITTEST 的精确命中测试穿透
        public const int WS_EX_MDICHILD = 0x00000040;                  // MDI 子窗口
        public const int WS_EX_TOOLWINDOW = 0x00000080;                // 工具窗口，通常不参与 Alt+Tab
        public const int WS_EX_WINDOWEDGE = 0x00000100;                // 窗口边缘样式
        public const int WS_EX_CLIENTEDGE = 0x00000200;                // 客户区凹陷边缘
        public const int WS_EX_CONTEXTHELP = 0x00000400;               // 标题栏帮助按钮
        public const int WS_EX_RIGHT = 0x00001000;                     // 右对齐窗口
        public const int WS_EX_LEFTSCROLLBAR = 0x00004000;             // 左侧滚动条
        public const int WS_EX_CONTROLPARENT = 0x00010000;             // 允许对子控件递归导航
        public const int WS_EX_STATICEDGE = 0x00020000;                // 静态边框样式
        public const int WS_EX_APPWINDOW = 0x00040000;                 // 强制显示在任务栏 / Alt+Tab 体系中
        public const int WS_EX_LAYERED = 0x00080000;                   // 分层窗口，支持透明/Alpha
        public const int WS_EX_NOINHERITLAYOUT = 0x00100000;           // 不继承窗口布局
        public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;       // 不使用重定向位图
        public const int WS_EX_LAYOUTRTL = 0x00400000;                 // RTL 布局
        public const int WS_EX_COMPOSITED = 0x02000000;                // 双缓冲合成绘制
        public const int WS_EX_NOACTIVATE = 0x08000000;                // 显示/点击时不激活窗口

        public const int WS_EX_OVERLAPPEDWINDOW =
            WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE;                       // 标准重叠窗口扩展样式组合

        #endregion GWL_EXSTYLE
    }
}