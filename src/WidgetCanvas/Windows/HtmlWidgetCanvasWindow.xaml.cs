#nullable enable

using WidgetCanvas.HtmlWidgets;
using WidgetCanvas.Infrastructure.Docking;
using WidgetCanvas.Infrastructure.Win32;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WidgetCanvas.Windows
{
    /// <summary>
    /// 可在全屏画布中创建、排列并持久化单文件 HTML 组件的浮岛窗口。
    /// </summary>
    public partial class HtmlWidgetCanvasWindow : Window
    {
        private const double MinimumWidgetWidth = 140;
        private const double MinimumWidgetHeight = 90;
        private const double SelectionThreshold = 8;
        private const double EdgeSnapDistance = 12;
        private const int SaveDelayMilliseconds = 800;
        private const int SaveRetryDelayMilliseconds = 3000;
        private const int LibrarySearchDelayMilliseconds = 160;
        private const int WebViewTransferGraceMilliseconds = 320;
        private const int MaximumWidgetHtmlBytes = 2 * 1024 * 1024;
        private const int MaximumProcessInputBytes = 1024 * 1024;
        private const uint KeyEventUpFlag = 0x0002;
        private static HtmlWidgetCanvasWindow? _instance;
        private static Task<CoreWebView2Environment>? _environmentTask;
        private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private readonly List<HtmlWidgetDefinition> _widgets;
        private readonly Dictionary<HtmlWidgetDefinition, WidgetRuntime> _runtimes = [];
        private readonly Dictionary<HtmlWidgetDefinition, HtmlWidgetWindow> _detachedWindows = [];
        private readonly HashSet<WebView2Control> _composingWebViews = [];
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _toastTimer;
        private readonly DispatcherTimer _overlayProbeTimer;
        private readonly DispatcherTimer _librarySearchTimer;
        private readonly string? _loadNotice;
        private readonly bool _recoveredFromBackup;
        private static string _dataFilePath = HtmlWidgetCanvasStore.DefaultFilePath;
        private static string _runtimeDataFilePath = HtmlWidgetCanvasStore.DefaultRuntimeFilePath;
        private WindowLocation _location = WindowLocation.FullScreen;
        private string _position = string.Empty;
        private bool _isSelecting;
        private bool _selectionMoved;
        private Point _selectionStart;
        private Rect _pendingBounds;
        private HtmlWidgetDefinition? _editingWidget;
        private bool _saveDirty;
        private bool _disposeRequested;
        private bool _visibilityChangePending;
        private bool _disposeAfterVisibilityChange;
        private bool _isLoadedOnce;
        private WidgetRuntime? _dragRuntime;
        private Point _dragStart;
        private double _dragStartX;
        private double _dragStartY;
        private bool _dragMoved;
        private WidgetRuntime? _resizeRuntime;
        private Point _resizeStart;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private Window? _controlOverlayWindow;
        private Canvas? _controlOverlayCanvas;
        private Border? _overlayToastPanel;
        private TextBlock? _overlayToastText;
        private WidgetRuntime? _hoverRuntime;
        private bool _overlayContextMenuOpen;
        private bool _isAdjustingWidget;
        private bool _returnToLibraryAfterEditor;
        private bool _returnToDetachedAfterEditor;
        private Window? _detachedEditorOwner;
        private HtmlWidgetReturnTarget _detachedEditorReturnTarget;
        private LibraryPreviewRuntime? _libraryPreviewRuntime;
        private HtmlWidgetDefinition? _libraryPreviewWidget;
        private int _libraryPreviewGeneration;
        private string? _lastSaveError;
        private bool _isClosed;
        private WindowDockEngine? _detachedDockEngine;

        /// <summary>
        /// 用户可编辑和备份的组件 HTML 数据文件。应在首次打开窗口前设置。
        /// </summary>
        public static string DataFilePath
        {
            get => _dataFilePath;
            set
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                if (_instance != null)
                    throw new InvalidOperationException("DataFilePath 必须在首次显示浮岛前设置。");
                _dataFilePath = Path.GetFullPath(value);
            }
        }

        /// <summary>
        /// 组件布局、窗口选项和实例状态文件。默认位于 LocalApplicationData，
        /// 与用户可编辑的组件 HTML 数据分开保存。
        /// </summary>
        public static string RuntimeDataFilePath
        {
            get => _runtimeDataFilePath;
            set
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                if (_instance != null)
                    throw new InvalidOperationException("RuntimeDataFilePath 必须在首次显示浮岛前设置。");
                _runtimeDataFilePath = Path.GetFullPath(value);
            }
        }

        /// <summary>
        /// 窗口显示时的定位方式。浮岛默认覆盖鼠标当前所在的完整屏幕。
        /// </summary>
        public WindowLocation Location
        {
            get => _location;
            set
            {
                _location = value;
                ApplyWindowLocationIfInitialized();
            }
        }

        /// <summary>
        /// 定位时使用的物理像素矩形或大小。
        /// <see cref="WindowLocation.FullScreen"/> 模式会忽略此值。
        /// </summary>
        public string Position
        {
            get => _position;
            set
            {
                _position = value ?? string.Empty;
                ApplyWindowLocationIfInitialized();
            }
        }

        static HtmlWidgetCanvasWindow()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private HtmlWidgetCanvasWindow()
        {
            InitializeComponent();
            HtmlWidgetCanvasLoadResult loadResult = HtmlWidgetCanvasStore.Load(
                DataFilePath,
                RuntimeDataFilePath);
            _widgets = loadResult.Widgets;
            _loadNotice = loadResult.Notice;
            _recoveredFromBackup = loadResult.RecoveredFromBackup;

            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SaveDelayMilliseconds)
            };
            _saveTimer.Tick += (_, _) => SaveNow();

            _toastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.8)
            };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                ToastPanel.Visibility = Visibility.Collapsed;
                if (_overlayToastPanel != null)
                    _overlayToastPanel.Visibility = Visibility.Collapsed;
            };

            _overlayProbeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _overlayProbeTimer.Tick += (_, _) => ProbeOverlayHover();

            _librarySearchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LibrarySearchDelayMilliseconds)
            };
            _librarySearchTimer.Tick += (_, _) =>
            {
                _librarySearchTimer.Stop();
                if (WidgetLibraryLayer.Visibility == Visibility.Visible)
                    RefreshWidgetLibrary();
            };

            if (NormalizeLoadedWidgetTitles())
                MarkDirty();

            Loaded += Window_Loaded;
            SourceInitialized += (_, _) => ApplyWindowLocation();
            Closing += Window_Closing;
            Closed += Window_Closed;
            LocationChanged += (_, _) => SyncControlOverlayBounds();
            SizeChanged += (_, _) =>
            {
                ClampAllWidgetsToCanvas();
                SyncControlOverlayBounds();
                UpdateAllOverlayControls();
            };
            IsVisibleChanged += (_, _) => ApplyOverlayWindowVisibility();
        }

        /// <summary>
        /// 显示进程内唯一的 HTML 浮岛窗口，并按需激活。
        /// </summary>
        public static HtmlWidgetCanvasWindow ShowWindow(Window? owner = null, bool activate = true)
        {
            return InvokeOnUiThread(() =>
            {
                HtmlWidgetCanvasWindow window = _instance ??= new HtmlWidgetCanvasWindow();
                window.Owner = owner is { IsLoaded: true } && owner != window
                    ? owner
                    : null;

                window.ApplyWindowLocationIfInitialized();
                window.ShowActivated = activate;
                if (!window.IsVisible)
                    window.Show();
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                if (activate)
                    window.Activate();
                return window;
            });
        }

        /// <summary>
        /// 按组件 HTML 的 <c>title</c> 打开独立组件窗口，而不显示主浮岛。
        /// </summary>
        /// <exception cref="KeyNotFoundException">找不到指定名称的组件。</exception>
        /// <exception cref="InvalidOperationException">存在多个同名组件。</exception>
        public static HtmlWidgetWindow ShowWidgetWindow(
            string componentName,
            bool activate = true) =>
            ShowWidgetWindowCore(componentName, owner: null, activate: activate);

        /// <summary>
        /// 按组件 HTML 的 <c>title</c> 打开独立组件窗口，并显式绑定 WPF owner。
        /// owner 最小化或关闭时，独立窗口会遵循 WPF 的标准所有权行为。
        /// </summary>
        public static HtmlWidgetWindow ShowWidgetWindow(
            string componentName,
            Window owner,
            bool activate = true)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (!owner.IsLoaded)
                throw new ArgumentException("owner 必须是已经显示的 WPF 窗口。", nameof(owner));
            return ShowWidgetWindowCore(componentName, owner, activate);
        }

        private static HtmlWidgetWindow ShowWidgetWindowCore(
            string componentName,
            Window? owner,
            bool activate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
            return InvokeOnUiThread(() =>
            {
                HtmlWidgetCanvasWindow host = _instance ??= new HtmlWidgetCanvasWindow();
                string name = componentName.Trim();
                List<HtmlWidgetDefinition> matches = host._widgets
                    .Where(widget => string.Equals(
                        GetDisplayName(widget),
                        name,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 0)
                    throw new KeyNotFoundException("找不到组件：" + name);
                if (matches.Count > 1)
                    throw new InvalidOperationException("存在多个同名组件，请先修改 title：" + name);
                return host.ShowDetachedWidget(
                    matches[0],
                    owner,
                    activate,
                    initialPosition: null,
                    hideCanvasAfterOpen: true,
                    applyOwner: true,
                    returnTarget: matches[0].Home == HtmlWidgetHome.Canvas
                        ? HtmlWidgetReturnTarget.Canvas
                        : HtmlWidgetReturnTarget.Library);
            });
        }

        /// <summary>
        /// 隐藏浮岛但保留所有 WebView2 实例。
        /// </summary>
        public static void HideWindow()
        {
            HtmlWidgetCanvasWindow? window = _instance;
            if (window == null)
                return;

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(HideWindow);
                return;
            }

            window.RequestVisibilityChange(dispose: false);
        }

        /// <summary>
        /// 真正关闭窗口并释放所有 WebView2。通常只在宿主退出时调用。
        /// </summary>
        public static void DisposeWindow()
        {
            HtmlWidgetCanvasWindow? window = _instance;
            if (window == null)
                return;

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.Invoke(DisposeWindow);
                return;
            }

            window.RequestVisibilityChange(dispose: true);
        }

        /// <summary>
        /// 以代码方式添加一个 HTML 组件，并返回最终的唯一 title。
        /// 若传入 title 已存在，宿主会自动追加序号。
        /// </summary>
        public string AddWidget(string html, Rect bounds)
        {
            Dispatcher.VerifyAccess();
            ArgumentException.ThrowIfNullOrWhiteSpace(html);
            if (bounds.IsEmpty ||
                !double.IsFinite(bounds.X) ||
                !double.IsFinite(bounds.Y) ||
                !double.IsFinite(bounds.Width) ||
                !double.IsFinite(bounds.Height))
            {
                throw new ArgumentOutOfRangeException(nameof(bounds));
            }
            string normalizedHtml = ExtractHtml(html);
            if (!IsValidHtmlDocument(normalizedHtml))
                throw new ArgumentException("必须提供包含 doctype、html、title、style 和 body 的完整单文件 HTML。", nameof(html));
            if (Encoding.UTF8.GetByteCount(normalizedHtml) > MaximumWidgetHtmlBytes)
                throw new ArgumentException("组件 HTML 的 UTF-8 大小不能超过 2 MB。", nameof(html));
            normalizedHtml = EnsureUniqueWidgetTitle(normalizedHtml);

            var widget = new HtmlWidgetDefinition
            {
                Html = normalizedHtml,
                X = Math.Max(0, bounds.X),
                Y = Math.Max(0, bounds.Y),
                Width = Math.Max(MinimumWidgetWidth, bounds.Width),
                Height = Math.Max(MinimumWidgetHeight, bounds.Height)
            };
            _widgets.Add(widget);
            if (_isLoadedOnce)
            {
                AddWidgetFrame(widget);
                ClampWidgetToCanvas(widget);
                if (WidgetEditorLayer.Visibility == Visibility.Visible ||
                    WidgetLibraryLayer.Visibility == Visibility.Visible)
                {
                    SetWebViewsVisible(false);
                }
            }
            UpdateEmptyState();
            RefreshWidgetLibrary();
            MarkDirty();
            return GetDisplayName(widget);
        }

        private HtmlWidgetWindow ShowDetachedWidget(
            HtmlWidgetDefinition widget,
            Window? owner,
            bool activate,
            string? initialPosition,
            bool hideCanvasAfterOpen,
            bool applyOwner = false,
            HtmlWidgetReturnTarget? returnTarget = null)
        {
            Dispatcher.VerifyAccess();
            // 隐藏的主浮岛同时承担独立窗口的数据与桥接宿主，不能继续从属于
            // 以前传入的 owner，否则旧 owner 关闭会连带销毁所有独立组件。
            if (Owner != null)
                Owner = null;

            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? existing))
            {
                if (applyOwner)
                    existing.ApplyOwner(owner);
                existing.ShowAndActivate(activate);
                if (hideCanvasAfterOpen && IsVisible)
                    HideWindow();
                return existing;
            }

            HtmlWidgetReturnTarget origin = returnTarget ??
                (widget.Home == HtmlWidgetHome.Canvas
                    ? HtmlWidgetReturnTarget.Canvas
                    : HtmlWidgetReturnTarget.Library);
            _runtimes.TryGetValue(widget, out WidgetRuntime? canvasRuntime);
            widget.Home = origin == HtmlWidgetReturnTarget.Canvas
                ? HtmlWidgetHome.Canvas
                : HtmlWidgetHome.Library;
            UpdateEmptyState();

            var window = new HtmlWidgetWindow(
                this,
                widget,
                initialPosition,
                deferBrowserStart: canvasRuntime != null,
                returnTarget: origin);
            window.ApplyOwner(owner);
            _detachedWindows.Add(widget, window);
            MarkDirty();
            SaveNow();
            RefreshWidgetLibrary();
            if (canvasRuntime != null &&
                _composingWebViews.Contains(canvasRuntime.WebView) &&
                IsActive)
            {
                CancelWidgetComposition();
            }
            window.ShowAndActivate(activate);
            if (hideCanvasAfterOpen && IsVisible)
                HideWindow();
            if (canvasRuntime != null)
                _ = CompleteCanvasDetachAsync(widget, canvasRuntime, window);
            return window;
        }

        private async Task CompleteCanvasDetachAsync(
            HtmlWidgetDefinition widget,
            WidgetRuntime canvasRuntime,
            HtmlWidgetWindow detachedWindow)
        {
            // 独立窗口先显示加载态，旧页面在不可见状态下短暂保留；旧控制器
            // 安全释放后才启动新页面。这样不会跨 HWND 搬运 WebView2，也不会让
            // 两份组件逻辑同时运行，并给输入法和刚发出的状态消息留出结束时间。
            await Task.Delay(WebViewTransferGraceMilliseconds);
            if (_isClosed ||
                !_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? currentWindow) ||
                currentWindow != detachedWindow ||
                !_runtimes.TryGetValue(widget, out WidgetRuntime? current) ||
                current != canvasRuntime)
            {
                return;
            }

            RemoveWidgetFrame(widget);
            detachedWindow.StartBrowserAfterTransfer();
        }

        private string CreateDetachedPosition(HtmlWidgetDefinition widget)
        {
            nint handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return widget.DetachedPosition;
            RECT canvasBounds = WindowHelper.GetWindowBounds(handle);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int left = canvasBounds.Left + (int)Math.Round(widget.X * dpi.DpiScaleX);
            int top = canvasBounds.Top + (int)Math.Round(widget.Y * dpi.DpiScaleY);
            int width = Math.Max(1, (int)Math.Round(widget.Width * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Round((widget.Height + 37) * dpi.DpiScaleY));
            return $"{left},{top},{width},{height}";
        }

        internal static string GetPhysicalWindowPosition(Window window)
        {
            nint handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return string.Empty;
            RECT bounds = WindowHelper.GetWindowBounds(handle);
            return bounds.IsEmpty
                ? string.Empty
                : $"{bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}";
        }

        internal string GetWidgetDisplayName(HtmlWidgetDefinition widget) => GetDisplayName(widget);

        internal void MarkWidgetDataDirty() => MarkDirty();

        internal void RegisterDetachedDocking(HtmlWidgetWindow window)
        {
            if (_isClosed || _disposeRequested || !window.IsVisible ||
                !window.Definition.DetachedAutoHide)
            {
                return;
            }

            nint handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            _detachedDockEngine ??= new WindowDockEngine(new WindowDockOptions
            {
                HideFromTaskbarWhenDocked = false
            });
            if (!_detachedDockEngine.Contains(handle))
                _detachedDockEngine.TryAdd(handle);
        }

        internal void UnregisterDetachedDocking(HtmlWidgetWindow window)
        {
            if (_detachedDockEngine == null)
                return;
            nint handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero && _detachedDockEngine.Contains(handle))
                _detachedDockEngine.Remove(handle);
        }

        internal bool CanSaveDetachedPosition(HtmlWidgetWindow window)
        {
            nint handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return false;
            if (_detachedDockEngine?.GetItems().Any(item =>
                    item.Handle == handle && item.IsDocked) == true)
            {
                return false;
            }

            RECT bounds = WindowHelper.GetWindowBounds(handle);
            if (bounds.IsEmpty)
                return false;
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            if (!WindowHelper.TryGetMonitorBoundsFromPoint(
                    centerX,
                    centerY,
                    out RECT monitor,
                    out _))
            {
                return false;
            }
            return bounds.Left >= monitor.Left && bounds.Top >= monitor.Top &&
                   bounds.Right <= monitor.Right && bounds.Bottom <= monitor.Bottom;
        }

        internal bool IsWebViewComposing(WebView2Control webView) =>
            _composingWebViews.Contains(webView);

        internal void ClearWebViewComposition(WebView2Control webView) =>
            _composingWebViews.Remove(webView);

        internal static void CancelWidgetComposition() => SendKey(0x1B);

        internal void DetachedWidgetClosed(
            HtmlWidgetDefinition widget,
            HtmlWidgetWindow window,
            HtmlWidgetWindow.DetachedCloseAction action)
        {
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? current) && current == window)
                _detachedWindows.Remove(widget);

            if (action == HtmlWidgetWindow.DetachedCloseAction.Host || _disposeRequested)
                return;

            if (action != HtmlWidgetWindow.DetachedCloseAction.Canvas &&
                _runtimes.TryGetValue(widget, out WidgetRuntime? canvasRuntime))
            {
                _ = ReleaseCanvasRuntimeAfterDetachedCloseAsync(widget, canvasRuntime, action);
            }

            if (action == HtmlWidgetWindow.DetachedCloseAction.Delete)
            {
                int originalIndex = _widgets.IndexOf(widget);
                if (originalIndex >= 0)
                    _widgets.RemoveAt(originalIndex);
                if (_libraryPreviewWidget == widget)
                    ResetLibraryPreview();
                UpdateEmptyState();
                RefreshWidgetLibrary();
                MarkDirty();
                if (!SaveNow())
                {
                    _widgets.Insert(Math.Clamp(originalIndex, 0, _widgets.Count), widget);
                    MarkDirty();
                    UpdateEmptyState();
                    RefreshWidgetLibrary();
                    ShowDeleteSaveFailure();
                }
                else if (IsVisible)
                {
                    ShowToast("组件已删除");
                }
                return;
            }

            if (action == HtmlWidgetWindow.DetachedCloseAction.Canvas)
                widget.Home = HtmlWidgetHome.Canvas;
            else if (action == HtmlWidgetWindow.DetachedCloseAction.Library)
                widget.Home = HtmlWidgetHome.Library;

            if (action == HtmlWidgetWindow.DetachedCloseAction.Canvas && _isLoadedOnce)
            {
                AddWidgetFrame(widget);
                ClampWidgetToCanvas(widget);
            }
            UpdateEmptyState();
            RefreshWidgetLibrary();
            MarkDirty();
            SaveNow();

            if (action is HtmlWidgetWindow.DetachedCloseAction.Canvas or
                HtmlWidgetWindow.DetachedCloseAction.Edit)
            {
                HtmlWidgetCanvasWindow canvas = ShowWindow(activate: true);
                if (canvas.WidgetLibraryLayer.Visibility == Visibility.Visible)
                    canvas.HideWidgetLibrary(restoreCanvas: action == HtmlWidgetWindow.DetachedCloseAction.Canvas);
                if (action == HtmlWidgetWindow.DetachedCloseAction.Edit)
                {
                    canvas.ShowWidgetEditor(
                        widget,
                        returnToDetached: true,
                        detachedOwner: window.Owner,
                        detachedReturnTarget: window.ReturnTarget);
                }
            }
        }

        private async Task ReleaseCanvasRuntimeAfterDetachedCloseAsync(
            HtmlWidgetDefinition widget,
            WidgetRuntime canvasRuntime,
            HtmlWidgetWindow.DetachedCloseAction action)
        {
            await Task.Delay(WebViewTransferGraceMilliseconds);
            if (_isClosed ||
                !_runtimes.TryGetValue(widget, out WidgetRuntime? current) ||
                current != canvasRuntime)
            {
                return;
            }
            if (action == HtmlWidgetWindow.DetachedCloseAction.Delete &&
                _widgets.Contains(widget) && widget.Home == HtmlWidgetHome.Canvas)
            {
                return;
            }

            RemoveWidgetFrame(widget);
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? detachedWindow))
                detachedWindow.StartBrowserAfterTransfer();
        }

        internal void DeleteDetachedWidget(
            HtmlWidgetDefinition widget,
            HtmlWidgetWindow window)
        {
            MessageBoxResult result = MessageBox.Show(
                window,
                "确定永久删除“" + GetDisplayName(widget) + "”吗？\n\n" +
                "组件代码和保存状态都会被删除，此操作无法撤销。",
                "删除组件",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            window.DeleteAfterConfirmation();
        }

        private void CloseDetachedWindowsForHost()
        {
            List<HtmlWidgetWindow> windows = _detachedWindows.Values.ToList();
            foreach (HtmlWidgetWindow window in windows)
                window.SaveLayoutForHost();
            foreach (HtmlWidgetWindow window in windows)
                window.CloseForHost();
            _detachedWindows.Clear();
            SaveNow();
        }

        private static T InvokeOnUiThread<T>(Func<T> action)
        {
            Dispatcher dispatcher = Application.Current?.Dispatcher
                ?? throw new InvalidOperationException("当前没有可用的 WPF UI 线程。");
            return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoadedOnce)
                return;

            _isLoadedOnce = true;
            CreateControlOverlayWindow();
            foreach (HtmlWidgetDefinition widget in _widgets.Where(item => item.Home == HtmlWidgetHome.Canvas))
                AddWidgetFrame(widget);
            ClampAllWidgetsToCanvas();
            UpdateEmptyState();
            Opacity = 1;
            ApplyOverlayWindowVisibility();
            if (_recoveredFromBackup)
                MarkDirty();
            if (!string.IsNullOrWhiteSpace(_loadNotice))
                ShowToast(_loadNotice);
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_disposeRequested || Dispatcher.HasShutdownStarted)
                return;

            e.Cancel = true;
            RequestVisibilityChange(dispose: false);
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            CloseDetachedWindowsForHost();
            SaveNow();
            _isClosed = true;
            _saveTimer.Stop();
            _toastTimer.Stop();
            _overlayProbeTimer.Stop();
            _librarySearchTimer.Stop();
            DisposeLibraryPreview();
            CloseControlOverlayWindow();
            foreach (WidgetRuntime runtime in _runtimes.Values)
                runtime.WebView.Dispose();
            _runtimes.Clear();
            _composingWebViews.Clear();
            _detachedDockEngine?.Dispose();
            _detachedDockEngine = null;
            _instance = null;
        }

        private async void RequestVisibilityChange(bool dispose)
        {
            if (dispose)
                _disposeAfterVisibilityChange = true;
            if (_visibilityChangePending)
                return;

            _visibilityChangePending = true;
            try
            {
                bool disposing = _disposeAfterVisibilityChange;
                bool activeComposition = disposing
                    ? IsActive && HasCanvasComposition() ||
                      _detachedWindows.Values.Any(window =>
                          window.IsActive && window.HasActiveComposition)
                    : IsActive && HasCanvasComposition();
                if (activeComposition && !Dispatcher.HasShutdownStarted)
                {
                    SendKey(0x1B);
                    await Task.Delay(300);
                    if (disposing)
                        _composingWebViews.Clear();
                    else
                        ClearCanvasComposition();
                }

                if (WidgetLibraryLayer.Visibility == Visibility.Visible)
                    ResetLibraryPreview();
                if (_disposeAfterVisibilityChange)
                {
                    _disposeAfterVisibilityChange = false;
                    CloseDetachedWindowsForHost();
                    SaveNow();
                    _disposeRequested = true;
                    Close();
                }
                else
                {
                    SaveNow();
                    HideControlOverlayWindow();
                    Hide();
                }
            }
            finally
            {
                _visibilityChangePending = false;
            }
        }

        private bool HasCanvasComposition() =>
            _runtimes.Values.Any(runtime => _composingWebViews.Contains(runtime.WebView)) ||
            _libraryPreviewRuntime is { } preview &&
            _composingWebViews.Contains(preview.WebView);

        private void ClearCanvasComposition()
        {
            foreach (WidgetRuntime runtime in _runtimes.Values)
                _composingWebViews.Remove(runtime.WebView);
            if (_libraryPreviewRuntime is { } preview)
                _composingWebViews.Remove(preview.WebView);
        }

        private static void SendKey(int virtualKey)
        {
            keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
            keybd_event((byte)virtualKey, 0, KeyEventUpFlag, UIntPtr.Zero);
        }

        private void ApplyWindowLocationIfInitialized()
        {
            if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
                ApplyWindowLocation();
        }

        private void ApplyWindowLocation() =>
            WindowHelper.SetLocation(this, Location, Position);

        private void CreateControlOverlayWindow()
        {
            if (_controlOverlayWindow != null)
                return;

            _controlOverlayCanvas = new Canvas();
            var overlayRoot = new Grid();
            overlayRoot.Children.Add(_controlOverlayCanvas);

            Style toolButtonStyle = (Style)FindResource("ToolButtonStyle");
            var closeButton = new Button
            {
                Content = "×",
                Width = 34,
                Height = 32,
                Padding = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 14, 0),
                ToolTip = "关闭浮岛",
                Style = toolButtonStyle,
                Focusable = false
            };
            closeButton.Click += CloseButton_Click;
            overlayRoot.Children.Add(closeButton);

            var copyPromptButton = new Button
            {
                Content = "复制 AI 提示词",
                ToolTip = "复制通用组件提示词，粘贴给 AI 后补充你的需求",
                Style = (Style)FindResource("AccentButtonStyle"),
                Focusable = false
            };
            copyPromptButton.Click += CopyGeneralPromptButton_Click;
            var libraryButton = new Button
            {
                Content = "组件库",
                ToolTip = "管理已保存的组件",
                Style = toolButtonStyle,
                Focusable = false
            };
            libraryButton.Click += (_, _) => ShowWidgetLibrary();
            var toolButtons = new StackPanel { Orientation = Orientation.Horizontal };
            toolButtons.Children.Add(copyPromptButton);
            toolButtons.Children.Add(libraryButton);

            var bottomDock = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 18),
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(210, 18, 27, 40)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(64, 123, 158, 202)),
                BorderThickness = new Thickness(1),
                Child = toolButtons
            };
            overlayRoot.Children.Add(bottomDock);

            _overlayToastText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(244, 247, 255)) };
            _overlayToastPanel = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 22, 0, 0),
                Padding = new Thickness(15, 8, 15, 8),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(229, 35, 46, 66)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(85, 127, 168, 220)),
                BorderThickness = new Thickness(1),
                Child = _overlayToastText
            };
            overlayRoot.Children.Add(_overlayToastPanel);

            var overlay = new Window
            {
                Owner = this,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = overlayRoot
            };
            overlay.SourceInitialized += (_, _) =>
            {
                // 这里只修改独立控制层自己的扩展样式，使它不激活且不出现在任务栏。
                // 不得在 WebView2 初始化期间修改承载浏览器的主窗口 HWND。
                nint handle = new WindowInteropHelper(overlay).Handle;
                int style = GetWindowLong(handle, ExtendedWindowStyleIndex);
                SetWindowLong(handle, ExtendedWindowStyleIndex,
                    style | ExtendedStyleNoActivate | ExtendedStyleToolWindow);
            };
            _controlOverlayWindow = overlay;
            SyncControlOverlayBounds();
        }

        private void SyncControlOverlayBounds()
        {
            if (_controlOverlayWindow == null)
                return;
            _controlOverlayWindow.Left = Left;
            _controlOverlayWindow.Top = Top;
            _controlOverlayWindow.Width = Math.Max(1, ActualWidth > 0 ? ActualWidth : Width);
            _controlOverlayWindow.Height = Math.Max(1, ActualHeight > 0 ? ActualHeight : Height);
            if (_controlOverlayCanvas != null)
            {
                _controlOverlayCanvas.Width = _controlOverlayWindow.Width;
                _controlOverlayCanvas.Height = _controlOverlayWindow.Height;
            }
        }

        private void ApplyOverlayWindowVisibility()
        {
            if (_controlOverlayWindow == null || !_isLoadedOnce)
                return;

            bool shouldShow = IsVisible &&
                              WidgetEditorLayer.Visibility != Visibility.Visible &&
                              WidgetLibraryLayer.Visibility != Visibility.Visible &&
                              !_isSelecting;
            if (!shouldShow)
            {
                HideControlOverlayWindow();
                return;
            }

            SyncControlOverlayBounds();
            if (!_controlOverlayWindow.IsVisible)
                _controlOverlayWindow.Show();
            _overlayProbeTimer.Start();
            ProbeOverlayHover();
        }

        private void HideControlOverlayWindow()
        {
            _overlayProbeTimer.Stop();
            SetHoveredRuntime(null);
            if (_controlOverlayWindow?.IsVisible == true)
                _controlOverlayWindow.Hide();
        }

        private void CloseControlOverlayWindow()
        {
            Window? overlay = _controlOverlayWindow;
            _controlOverlayWindow = null;
            _controlOverlayCanvas = null;
            _overlayToastPanel = null;
            _overlayToastText = null;
            if (overlay == null)
                return;
            try
            {
                overlay.Owner = null;
                overlay.Close();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ProbeOverlayHover()
        {
            if (_overlayContextMenuOpen || _isAdjustingWidget ||
                _controlOverlayWindow?.IsVisible != true)
            {
                return;
            }
            if (!GetCursorPos(out NativePoint cursor))
                return;

            Point point;
            try
            {
                point = PointFromScreen(new Point(cursor.X, cursor.Y));
            }
            catch (InvalidOperationException)
            {
                return;
            }

            WidgetRuntime? hit = null;
            for (int index = _widgets.Count - 1; index >= 0; index--)
            {
                if (!_runtimes.TryGetValue(_widgets[index], out WidgetRuntime? runtime))
                    continue;
                var bounds = new Rect(
                    runtime.Definition.X,
                    runtime.Definition.Y,
                    runtime.Definition.Width,
                    runtime.Definition.Height);
                if (bounds.Contains(point))
                {
                    hit = runtime;
                    break;
                }
            }
            SetHoveredRuntime(hit);
        }

        private void SetHoveredRuntime(WidgetRuntime? runtime)
        {
            if (_hoverRuntime == runtime)
                return;
            if (_hoverRuntime != null)
                SetOverlayControlsVisible(_hoverRuntime, false);
            _hoverRuntime = runtime;
            if (runtime != null)
                SetOverlayControlsVisible(runtime, true);
        }

        private static void SetOverlayControlsVisible(WidgetRuntime runtime, bool visible)
        {
            if (runtime.DragStrip == null || runtime.ResizeHandle == null)
                return;
            runtime.DragStrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            runtime.ResizeHandle.Visibility = visible && !runtime.Definition.IsLocked
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateAllOverlayControls()
        {
            foreach (WidgetRuntime runtime in _runtimes.Values)
                UpdateOverlayControls(runtime);
        }

        private static void UpdateOverlayControls(WidgetRuntime runtime)
        {
            if (runtime.DragStrip == null || runtime.ResizeHandle == null)
                return;
            Canvas.SetLeft(runtime.DragStrip,
                runtime.Definition.X + (runtime.Definition.Width - runtime.DragStrip.Width) / 2);
            Canvas.SetTop(runtime.DragStrip, Math.Max(3, runtime.Definition.Y - 7));
            Canvas.SetLeft(runtime.ResizeHandle,
                runtime.Definition.X + runtime.Definition.Width - runtime.ResizeHandle.Width - 2);
            Canvas.SetTop(runtime.ResizeHandle,
                runtime.Definition.Y + runtime.Definition.Height - runtime.ResizeHandle.Height - 2);
        }

        private void BeginWidgetAdjustment()
        {
            _isAdjustingWidget = true;
        }

        private void EndWidgetAdjustment()
        {
            _isAdjustingWidget = false;
            ProbeOverlayHover();
        }

        private void WidgetCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WidgetEditorLayer.Visibility == Visibility.Visible || IsInsideWidget(e.OriginalSource as DependencyObject))
                return;

            _isSelecting = true;
            _selectionMoved = false;
            _selectionStart = e.GetPosition(WidgetCanvas);
            Canvas.SetLeft(SelectionRectangle, _selectionStart.X);
            Canvas.SetTop(SelectionRectangle, _selectionStart.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectionSizeBadge.Visibility = Visibility.Collapsed;
            WidgetCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void WidgetCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting)
                return;

            Point current = e.GetPosition(WidgetCanvas);
            double width = Math.Abs(current.X - _selectionStart.X);
            double height = Math.Abs(current.Y - _selectionStart.Y);
            if (!_selectionMoved && width < SelectionThreshold && height < SelectionThreshold)
                return;

            _selectionMoved = true;
            SetWebViewsVisible(false);
            HideControlOverlayWindow();
            SelectionRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRectangle, Math.Min(current.X, _selectionStart.X));
            Canvas.SetTop(SelectionRectangle, Math.Min(current.Y, _selectionStart.Y));
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
            SelectionSizeText.Text = $"{width:0} × {height:0}";
            SelectionSizeBadge.Visibility = Visibility.Visible;
            double left = Math.Min(current.X, _selectionStart.X);
            double top = Math.Min(current.Y, _selectionStart.Y);
            Canvas.SetLeft(SelectionSizeBadge, left);
            Canvas.SetTop(SelectionSizeBadge, top >= 32 ? top - 30 : top + 8);
        }

        private void WidgetCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
                return;

            _isSelecting = false;
            WidgetCanvas.ReleaseMouseCapture();
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectionSizeBadge.Visibility = Visibility.Collapsed;

            if (!_selectionMoved)
            {
                SetWebViewsVisible(true);
                HideWindow();
                e.Handled = true;
                return;
            }

            double width = SelectionRectangle.Width;
            double height = SelectionRectangle.Height;
            if (width < MinimumWidgetWidth || height < MinimumWidgetHeight)
            {
                SetWebViewsVisible(true);
                ApplyOverlayWindowVisibility();
                ShowToast($"组件至少需要 {MinimumWidgetWidth:0} × {MinimumWidgetHeight:0}");
                return;
            }

            _pendingBounds = new Rect(
                Canvas.GetLeft(SelectionRectangle),
                Canvas.GetTop(SelectionRectangle),
                width,
                height);
            ShowWidgetEditor(null);
            e.Handled = true;
        }

        private static bool IsInsideWidget(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is FrameworkElement { Tag: "HtmlWidgetFrame" })
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void AddWidgetFrame(HtmlWidgetDefinition widget)
        {
            if (widget.Home != HtmlWidgetHome.Canvas ||
                _detachedWindows.ContainsKey(widget) ||
                _runtimes.ContainsKey(widget))
                return;

            var frame = new Border
            {
                Tag = "HtmlWidgetFrame",
                DataContext = widget,
                Width = widget.Width,
                Height = widget.Height,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                ClipToBounds = true
            };

            var webView = new WebView2Control
            {
                DefaultBackgroundColor = System.Drawing.Color.Transparent,
                AllowExternalDrop = false
            };
            var contentRoot = new Grid();
            contentRoot.Children.Add(webView);
            frame.Child = contentRoot;
            WidgetCanvas.Children.Add(frame);
            Canvas.SetLeft(frame, widget.X);
            Canvas.SetTop(frame, widget.Y);
            Canvas.SetZIndex(frame, _widgets.IndexOf(widget) + 1);

            var runtime = new WidgetRuntime(widget, frame, webView, contentRoot);
            _runtimes.Add(widget, runtime);
            AddWidgetErrorPanel(runtime);
            AddOverlayControls(runtime);
            ApplyLockState(runtime);
            _ = InitializeWebViewAsync(runtime);
        }

        private void AddWidgetErrorPanel(WidgetRuntime runtime)
        {
            var title = new TextBlock
            {
                Text = "组件加载失败",
                Foreground = new SolidColorBrush(Color.FromRgb(244, 232, 235)),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var detail = new TextBlock
            {
                Text = "可以重试或编辑组件代码",
                Foreground = new SolidColorBrush(Color.FromRgb(172, 145, 154)),
                FontSize = 10.5,
                Margin = new Thickness(0, 5, 0, 9),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var retryButton = new Button
            {
                Content = "重试",
                Padding = new Thickness(10, 4, 10, 4),
                Focusable = false,
                Style = (Style)FindResource("AccentButtonStyle")
            };
            retryButton.Click += (_, _) => ReloadWidget(runtime.Definition);
            var editButton = new Button
            {
                Content = "编辑",
                Padding = new Thickness(10, 4, 10, 4),
                Focusable = false,
                Style = (Style)FindResource("ToolButtonStyle")
            };
            editButton.Click += (_, _) => ShowWidgetEditor(runtime.Definition);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            actions.Children.Add(retryButton);
            actions.Children.Add(editButton);
            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(title);
            content.Children.Add(detail);
            content.Children.Add(actions);
            var panel = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Color.FromArgb(246, 28, 24, 31)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(145, 204, 103, 119)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Child = content
            };
            runtime.ErrorPanel = panel;
            runtime.ErrorText = detail;
            runtime.ContentRoot.Children.Add(panel);
        }

        private void ShowFatalWidgetError(
            WidgetRuntime runtime,
            string message,
            string? diagnosticDetails = null)
        {
            if (!_runtimes.TryGetValue(runtime.Definition, out WidgetRuntime? current) || current != runtime)
                return;

            runtime.HasFatalError = true;
            string details = string.IsNullOrWhiteSpace(diagnosticDetails)
                ? message
                : diagnosticDetails.Trim();
            SetWidgetErrorState(runtime, details);
            runtime.WebView.Visibility = Visibility.Hidden;
            runtime.WebView.IsHitTestVisible = false;
            if (runtime.ErrorPanel != null)
                runtime.ErrorPanel.Visibility = Visibility.Visible;
            if (runtime.ErrorText != null)
            {
                runtime.ErrorText.Text = GetShortWidgetError(message);
                runtime.ErrorText.ToolTip = details;
            }
        }

        private void MarkWidgetRuntimeError(WidgetRuntime runtime, string message)
        {
            if (!_runtimes.TryGetValue(runtime.Definition, out WidgetRuntime? current) || current != runtime)
                return;
            if (runtime.HasFatalError)
                return;
            SetWidgetErrorState(runtime, message);
        }

        private static void SetWidgetErrorState(WidgetRuntime runtime, string message)
        {
            runtime.LastError = string.IsNullOrWhiteSpace(message) ? "未知组件错误" : message.Trim();
            if (runtime.DragStrip != null)
            {
                runtime.DragStrip.BorderBrush = new SolidColorBrush(Color.FromArgb(190, 230, 112, 129));
                runtime.DragStrip.ToolTip = "组件发生错误 · 右键查看";
            }
        }

        private void ClearWidgetError(WidgetRuntime runtime)
        {
            if (!_runtimes.TryGetValue(runtime.Definition, out WidgetRuntime? current) || current != runtime)
                return;

            runtime.LastError = null;
            runtime.HasFatalError = false;
            if (runtime.ErrorPanel != null)
                runtime.ErrorPanel.Visibility = Visibility.Collapsed;
            if (runtime.DragStrip != null)
            {
                runtime.DragStrip.BorderBrush = new SolidColorBrush(Color.FromArgb(86, 126, 169, 220));
                ApplyLockState(runtime);
            }
            bool canShow = WidgetEditorLayer.Visibility != Visibility.Visible &&
                           WidgetLibraryLayer.Visibility != Visibility.Visible;
            runtime.WebView.Visibility = canShow ? Visibility.Visible : Visibility.Hidden;
            runtime.WebView.IsHitTestVisible = canShow;
        }

        private void ShowWidgetErrorDetails(WidgetRuntime runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime.LastError))
                return;

            HideControlOverlayWindow();
            MessageBox.Show(
                this,
                runtime.LastError,
                GetDisplayName(runtime.Definition) + " · 错误信息",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ApplyOverlayWindowVisibility();
        }

        private void ShowWidgetHostErrorDetails(WidgetRuntime runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime.LastHostError))
                return;

            HideControlOverlayWindow();
            MessageBox.Show(
                this,
                runtime.LastHostError,
                GetDisplayName(runtime.Definition) + " · 宿主调用错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ApplyOverlayWindowVisibility();
        }

        internal static string GetShortWidgetError(string message)
        {
            string value = string.IsNullOrWhiteSpace(message) ? "未知错误" : message.Trim();
            int lineBreak = value.IndexOfAny(['\r', '\n']);
            if (lineBreak > 0)
                value = value[..lineBreak];
            return value.Length <= 56 ? value : value[..55] + "…";
        }

        private void AddOverlayControls(WidgetRuntime runtime)
        {
            if (_controlOverlayCanvas == null || runtime.DragStrip != null)
                return;

            var dragText = new TextBlock
            {
                Text = "⠿",
                Foreground = new SolidColorBrush(Color.FromRgb(198, 213, 233)),
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var dragStrip = new Border
            {
                Width = 30,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(166, 18, 27, 40)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(86, 126, 169, 220)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.SizeAll,
                Child = dragText,
                Visibility = Visibility.Collapsed,
                ToolTip = "拖动移动 · Ctrl+拖动弹出窗口 · 右键管理"
            };
            var resizeHandle = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(142, 25, 35, 51)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 126, 169, 220)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.SizeNWSE,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "◢",
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(155, 196, 239)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            runtime.DragStrip = dragStrip;
            runtime.DragText = dragText;
            runtime.ResizeHandle = resizeHandle;
            _controlOverlayCanvas.Children.Add(dragStrip);
            _controlOverlayCanvas.Children.Add(resizeHandle);
            Canvas.SetZIndex(dragStrip, 10);
            Canvas.SetZIndex(resizeHandle, 11);
            UpdateOverlayControls(runtime);

            dragStrip.MouseLeftButtonDown += (_, e) =>
            {
                if (runtime.Definition.IsLocked)
                    return;
                BringToFront(runtime);
                BeginWidgetAdjustment();
                _dragRuntime = runtime;
                _dragStart = e.GetPosition(_controlOverlayCanvas);
                _dragStartX = runtime.Definition.X;
                _dragStartY = runtime.Definition.Y;
                _dragMoved = false;
                dragStrip.CaptureMouse();
                e.Handled = true;
            };
            dragStrip.MouseMove += (_, e) =>
            {
                if (_dragRuntime != runtime || e.LeftButton != MouseButtonState.Pressed)
                    return;
                Point current = e.GetPosition(_controlOverlayCanvas);
                if (!_dragMoved &&
                    (Math.Abs(current.X - _dragStart.X) >= SelectionThreshold ||
                     Math.Abs(current.Y - _dragStart.Y) >= SelectionThreshold))
                {
                    _dragMoved = true;
                }
                MoveWidget(runtime,
                    _dragStartX + current.X - _dragStart.X,
                    _dragStartY + current.Y - _dragStart.Y);
            };
            dragStrip.MouseLeftButtonUp += (_, e) =>
            {
                if (_dragRuntime != runtime)
                    return;
                bool detach = _dragMoved &&
                              Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                _dragRuntime = null;
                _dragMoved = false;
                dragStrip.ReleaseMouseCapture();
                EndWidgetAdjustment();
                if (detach)
                {
                    string initialPosition = CreateDetachedPosition(runtime.Definition);
                    ShowDetachedWidget(
                        runtime.Definition,
                        owner: null,
                        activate: true,
                        initialPosition: initialPosition,
                        hideCanvasAfterOpen: true,
                        returnTarget: HtmlWidgetReturnTarget.Canvas);
                }
                else
                {
                    MarkDirty();
                }
                e.Handled = true;
            };
            dragStrip.LostMouseCapture += (_, _) =>
            {
                if (_dragRuntime != runtime)
                    return;
                _dragRuntime = null;
                _dragMoved = false;
                EndWidgetAdjustment();
                MarkDirty();
            };
            dragStrip.MouseRightButtonUp += (_, e) =>
            {
                OpenWidgetMenu(runtime, dragStrip);
                e.Handled = true;
            };

            resizeHandle.MouseLeftButtonDown += (_, e) =>
            {
                if (runtime.Definition.IsLocked)
                    return;
                BringToFront(runtime);
                BeginWidgetAdjustment();
                _resizeRuntime = runtime;
                _resizeStart = e.GetPosition(_controlOverlayCanvas);
                _resizeStartWidth = runtime.Definition.Width;
                _resizeStartHeight = runtime.Definition.Height;
                resizeHandle.CaptureMouse();
                e.Handled = true;
            };
            resizeHandle.MouseMove += (_, e) =>
            {
                if (_resizeRuntime != runtime || e.LeftButton != MouseButtonState.Pressed)
                    return;
                Point current = e.GetPosition(_controlOverlayCanvas);
                ResizeWidget(runtime,
                    _resizeStartWidth + current.X - _resizeStart.X,
                    _resizeStartHeight + current.Y - _resizeStart.Y);
            };
            resizeHandle.MouseLeftButtonUp += (_, e) =>
            {
                if (_resizeRuntime != runtime)
                    return;
                _resizeRuntime = null;
                resizeHandle.ReleaseMouseCapture();
                EndWidgetAdjustment();
                MarkDirty();
                e.Handled = true;
            };
            resizeHandle.LostMouseCapture += (_, _) =>
            {
                if (_resizeRuntime != runtime)
                    return;
                _resizeRuntime = null;
                EndWidgetAdjustment();
                MarkDirty();
            };
        }

        private void OpenWidgetMenu(WidgetRuntime runtime, FrameworkElement target)
        {
            var menu = new ContextMenu();
            HtmlWidgetMenuTheme.Apply(menu);
            menu.Items.Add(CreateMenuItem("编辑组件…", () => ShowWidgetEditor(runtime.Definition)));
            menu.Items.Add(CreateMenuItem("复制 AI 修改提示词", () => CopyEditPrompt(runtime.Definition)));
            menu.Items.Add(CreateMenuItem("重新加载", () => ReloadWidget(runtime.Definition)));
            MenuItem? diagnosticMenu = CreateDiagnosticMenu(runtime);
            if (diagnosticMenu != null)
                menu.Items.Add(diagnosticMenu);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(
                runtime.Definition.IsLocked ? "解除位置锁定" : "锁定位置",
                () =>
                {
                    runtime.Definition.IsLocked = !runtime.Definition.IsLocked;
                    ApplyLockState(runtime);
                    MarkDirty();
                }));
            menu.Items.Add(CreateMenuItem(
                "弹出为独立窗口",
                () => ShowDetachedWidget(
                    runtime.Definition,
                    owner: null,
                    activate: true,
                    initialPosition: CreateDetachedPosition(runtime.Definition),
                    hideCanvasAfterOpen: true,
                    returnTarget: HtmlWidgetReturnTarget.Canvas)));
            menu.Items.Add(CreateMenuItem("收进组件库", () => ArchiveWidget(runtime.Definition)));
            menu.Items.Add(CreateSubmenu(
                "更多操作",
                CreateMenuItem("用剪贴板 HTML 更新", () => UpdateWidgetFromClipboard(runtime.Definition)),
                CreateMenuItem("复制 HTML", () => CopyText(runtime.Definition.Html, "HTML 已复制")),
                CreateMenuItem("创建组件副本", () => DuplicateWidget(runtime.Definition)),
                new Separator(),
                CreateMenuItem("移到组件最上层", () => BringToFront(runtime))));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateDangerMenuItem(
                "删除组件…",
                () => DeleteWidgetPermanently(runtime.Definition)));
            menu.PlacementTarget = target;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.Opened += (_, _) =>
            {
                _overlayContextMenuOpen = true;
                SetHoveredRuntime(runtime);
            };
            menu.Closed += (_, _) =>
            {
                _overlayContextMenuOpen = false;
                ProbeOverlayHover();
            };
            menu.IsOpen = true;
        }

        private MenuItem? CreateDiagnosticMenu(WidgetRuntime runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime.LastError) &&
                string.IsNullOrWhiteSpace(runtime.LastHostError))
            {
                return null;
            }

            var menu = new MenuItem { Header = "错误诊断" };
            if (!string.IsNullOrWhiteSpace(runtime.LastError))
            {
                menu.Items.Add(CreateMenuItem("查看组件错误…", () => ShowWidgetErrorDetails(runtime)));
                menu.Items.Add(CreateMenuItem(
                    "复制组件错误",
                    () => CopyText(runtime.LastError, "错误信息已复制")));
            }
            if (!string.IsNullOrWhiteSpace(runtime.LastHostError))
            {
                if (menu.Items.Count > 0)
                    menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem(
                    "查看最近宿主调用错误…",
                    () => ShowWidgetHostErrorDetails(runtime)));
                menu.Items.Add(CreateMenuItem(
                    "复制最近宿主调用错误",
                    () => CopyText(runtime.LastHostError, "宿主调用错误已复制")));
            }
            return menu;
        }

        private static MenuItem CreateMenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        private static MenuItem CreateSubmenu(string header, params object[] items)
        {
            var menu = new MenuItem { Header = header };
            foreach (object item in items)
                menu.Items.Add(item);
            return menu;
        }

        private static MenuItem CreateDangerMenuItem(string header, Action action)
        {
            MenuItem item = CreateMenuItem(header, action);
            item.Foreground = new SolidColorBrush(Color.FromRgb(255, 157, 165));
            return item;
        }

        private void UpdateWidgetFromClipboard(HtmlWidgetDefinition widget)
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    ShowToast("剪贴板中没有文本");
                    return;
                }
                string html = ExtractHtml(Clipboard.GetText());
                if (!IsValidHtmlDocument(html))
                {
                    ShowToast("剪贴板中没有完整单文件 HTML");
                    return;
                }
                if (Encoding.UTF8.GetByteCount(html) > MaximumWidgetHtmlBytes)
                {
                    ShowToast("组件 HTML 不能超过 2 MB");
                    return;
                }
                html = EnsureUniqueWidgetTitle(html, widget);
                string previousHtml = widget.Html;
                widget.Html = html;
                MarkDirty();
                if (!SaveNow())
                {
                    widget.Html = previousHtml;
                    MarkDirty();
                    ShowToast("更新失败，原组件已保留");
                    return;
                }
                ReloadWidget(widget);
                if (_libraryPreviewWidget == widget)
                    ShowLibraryWidgetPreview(widget, forceReload: true);
                ShowToast("组件已更新");
            }
            catch (Exception ex)
            {
                ShowToast("更新失败：" + ex.Message);
            }
        }

        private void CopyEditPrompt(HtmlWidgetDefinition widget)
        {
            if (CopyText(HtmlWidgetCanvasPrompt.BuildEdit(widget), "AI 修改提示词已复制"))
                _ = Dispatcher.BeginInvoke(HideWindow);
        }

        private bool CopyText(string text, string successMessage)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                ShowToast(successMessage);
                return true;
            }
            catch (Exception ex)
            {
                ShowToast("复制失败：" + ex.Message);
                return false;
            }
        }

        private void ApplyLockState(WidgetRuntime runtime)
        {
            if (runtime.DragStrip == null || runtime.DragText == null || runtime.ResizeHandle == null)
                return;
            runtime.DragStrip.Cursor = runtime.Definition.IsLocked ? Cursors.Arrow : Cursors.SizeAll;
            runtime.DragStrip.ToolTip = runtime.Definition.IsLocked
                ? "位置已锁定 · 右键管理组件"
                : "拖动移动 · Ctrl+拖动弹出窗口 · 右键管理";
            runtime.DragText.Text = runtime.Definition.IsLocked
                ? "●"
                : "⠿";
            runtime.ResizeHandle.Visibility = runtime.Definition.IsLocked || _hoverRuntime != runtime
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void MoveWidget(WidgetRuntime runtime, double x, double y)
        {
            double maxX = Math.Max(0, WidgetCanvas.ActualWidth - runtime.Definition.Width);
            double maxY = Math.Max(0, WidgetCanvas.ActualHeight - runtime.Definition.Height);
            x = SnapToEdge(Math.Clamp(x, 0, maxX), maxX);
            y = SnapToEdge(Math.Clamp(y, 0, maxY), maxY);
            runtime.Definition.X = x;
            runtime.Definition.Y = y;
            Canvas.SetLeft(runtime.Frame, x);
            Canvas.SetTop(runtime.Frame, y);
            UpdateOverlayControls(runtime);
        }

        private void ResizeWidget(WidgetRuntime runtime, double width, double height)
        {
            double maxWidth = Math.Max(MinimumWidgetWidth, WidgetCanvas.ActualWidth - runtime.Definition.X);
            double maxHeight = Math.Max(MinimumWidgetHeight, WidgetCanvas.ActualHeight - runtime.Definition.Y);
            width = SnapToEdge(Math.Clamp(width, MinimumWidgetWidth, maxWidth), maxWidth);
            height = SnapToEdge(Math.Clamp(height, MinimumWidgetHeight, maxHeight), maxHeight);
            runtime.Definition.Width = width;
            runtime.Definition.Height = height;
            runtime.Frame.Width = width;
            runtime.Frame.Height = height;
            UpdateOverlayControls(runtime);
        }

        private static double SnapToEdge(double value, double maximum)
        {
            return maximum - value <= EdgeSnapDistance ? maximum : value;
        }

        private void BringToFront(WidgetRuntime runtime)
        {
            _widgets.Remove(runtime.Definition);
            _widgets.Add(runtime.Definition);
            int z = 1;
            foreach (HtmlWidgetDefinition widget in _widgets)
            {
                if (_runtimes.TryGetValue(widget, out WidgetRuntime? itemRuntime))
                {
                    Canvas.SetZIndex(itemRuntime.Frame, z++);
                    if (itemRuntime.DragStrip != null)
                        Canvas.SetZIndex(itemRuntime.DragStrip, z * 2 + 10);
                    if (itemRuntime.ResizeHandle != null)
                        Canvas.SetZIndex(itemRuntime.ResizeHandle, z * 2 + 11);
                }
            }
            MarkDirty();
        }

        private void DuplicateWidget(HtmlWidgetDefinition source, bool placeOnCanvas = true)
        {
            string sourceTitle = GetDisplayName(source);
            string copyHtml = SetHtmlTitle(source.Html, sourceTitle + " 副本");
            copyHtml = EnsureUniqueWidgetTitle(copyHtml);
            var copy = new HtmlWidgetDefinition
            {
                Html = copyHtml,
                X = source.X + 28,
                Y = source.Y + 28,
                Width = source.Width,
                Height = source.Height,
                Home = placeOnCanvas ? HtmlWidgetHome.Canvas : HtmlWidgetHome.Library
            };
            HtmlWidgetCanvasStore.Normalize(copy);
            _widgets.Add(copy);
            if (placeOnCanvas)
            {
                AddWidgetFrame(copy);
                ClampWidgetToCanvas(copy);
            }
            UpdateEmptyState();
            RefreshWidgetLibrary();
            MarkDirty();
        }

        private async void ArchiveWidget(HtmlWidgetDefinition widget)
        {
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? detachedWindow))
            {
                detachedWindow.MoveToLibrary();
                return;
            }
            if (widget.Home != HtmlWidgetHome.Canvas)
                return;

            await PrepareCanvasRuntimeForDisposalAsync(widget);
            if (widget.Home != HtmlWidgetHome.Canvas || _detachedWindows.ContainsKey(widget))
                return;
            widget.Home = HtmlWidgetHome.Library;
            RemoveWidgetFrame(widget);
            UpdateEmptyState();
            RefreshWidgetLibrary();
            MarkDirty();
            ShowToast("已收进组件库");
        }

        private void RestoreWidget(HtmlWidgetDefinition widget)
        {
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? detachedWindow))
            {
                detachedWindow.ReturnToCanvas();
                return;
            }
            if (widget.Home == HtmlWidgetHome.Canvas)
                return;

            HideWidgetLibrary();
            widget.Home = HtmlWidgetHome.Canvas;
            AddWidgetFrame(widget);
            ClampWidgetToCanvas(widget);
            if (_runtimes.TryGetValue(widget, out WidgetRuntime? runtime))
                BringToFront(runtime);
            UpdateEmptyState();
            MarkDirty();
            ShowToast("组件已放回浮岛");
        }

        private async void DeleteWidgetPermanently(HtmlWidgetDefinition widget)
        {
            if (!_widgets.Contains(widget))
                return;

            HideControlOverlayWindow();
            try
            {
                MessageBoxResult result = MessageBox.Show(this,
                    "确定永久删除“" + GetDisplayName(widget) + "”吗？\n\n" +
                    "组件代码和保存状态都会被删除，此操作无法撤销。",
                    "删除组件",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;

                if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? detachedWindow))
                {
                    detachedWindow.DeleteAfterConfirmation();
                    return;
                }

                await PrepareCanvasRuntimeForDisposalAsync(widget);
                if (!_widgets.Contains(widget))
                    return;
                if (_detachedWindows.TryGetValue(widget, out detachedWindow))
                {
                    detachedWindow.DeleteAfterConfirmation();
                    return;
                }
                int originalIndex = _widgets.IndexOf(widget);
                bool wasOnCanvas = widget.Home == HtmlWidgetHome.Canvas;
                RemoveWidget(widget);
                if (_libraryPreviewWidget == widget)
                    ResetLibraryPreview();
                MarkDirty();
                bool saved = SaveNow();
                if (!saved)
                {
                    _widgets.Insert(Math.Clamp(originalIndex, 0, _widgets.Count), widget);
                    if (wasOnCanvas && _isLoadedOnce)
                    {
                        AddWidgetFrame(widget);
                        ClampWidgetToCanvas(widget);
                    }
                    UpdateEmptyState();
                    MarkDirty();
                }
                RefreshWidgetLibrary();
                if (saved)
                    ShowToast("组件已删除");
                else
                    ShowDeleteSaveFailure();
            }
            finally
            {
                ApplyOverlayWindowVisibility();
            }
        }

        private void RemoveWidget(HtmlWidgetDefinition widget)
        {
            RemoveWidgetFrame(widget);
            _widgets.Remove(widget);
            UpdateEmptyState();
        }

        private void ShowDeleteSaveFailure()
        {
            const string message = "组件数据无法保存，删除已取消，组件仍然保留。";
            if (IsVisible)
            {
                MessageBox.Show(
                    this,
                    message,
                    "删除失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(
                    message,
                    "删除失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemoveWidgetFrame(HtmlWidgetDefinition widget)
        {
            if (_runtimes.Remove(widget, out WidgetRuntime? runtime))
            {
                _composingWebViews.Remove(runtime.WebView);
                bool wasAdjusting = _dragRuntime == runtime || _resizeRuntime == runtime;
                if (_dragRuntime == runtime)
                {
                    _dragRuntime = null;
                    runtime.DragStrip?.ReleaseMouseCapture();
                }
                if (_resizeRuntime == runtime)
                {
                    _resizeRuntime = null;
                    runtime.ResizeHandle?.ReleaseMouseCapture();
                }
                if (wasAdjusting)
                    EndWidgetAdjustment();
                WidgetCanvas.Children.Remove(runtime.Frame);
                if (_controlOverlayCanvas != null)
                {
                    if (runtime.DragStrip != null)
                        _controlOverlayCanvas.Children.Remove(runtime.DragStrip);
                    if (runtime.ResizeHandle != null)
                        _controlOverlayCanvas.Children.Remove(runtime.ResizeHandle);
                }
                if (_hoverRuntime == runtime)
                    _hoverRuntime = null;
                runtime.WebView.Dispose();
            }
        }

        private async Task PrepareCanvasRuntimeForDisposalAsync(HtmlWidgetDefinition widget)
        {
            if (!_runtimes.TryGetValue(widget, out WidgetRuntime? runtime) ||
                !_composingWebViews.Contains(runtime.WebView))
            {
                return;
            }

            if (IsActive && runtime.WebView.IsKeyboardFocusWithin)
                CancelWidgetComposition();
            await Task.Delay(300);
            _composingWebViews.Remove(runtime.WebView);
        }

        private void ClampAllWidgetsToCanvas()
        {
            if (!_isLoadedOnce || WidgetCanvas.ActualWidth <= 0 || WidgetCanvas.ActualHeight <= 0)
                return;
            bool changed = false;
            foreach (HtmlWidgetDefinition widget in _widgets)
                changed |= ClampWidgetToCanvas(widget);
            if (changed)
                MarkDirty();
        }

        private bool ClampWidgetToCanvas(HtmlWidgetDefinition widget)
        {
            if (!_runtimes.TryGetValue(widget, out WidgetRuntime? runtime))
                return false;

            double oldX = widget.X;
            double oldY = widget.Y;
            double oldWidth = widget.Width;
            double oldHeight = widget.Height;
            widget.Width = Math.Min(widget.Width, Math.Max(MinimumWidgetWidth, WidgetCanvas.ActualWidth));
            widget.Height = Math.Min(widget.Height, Math.Max(MinimumWidgetHeight, WidgetCanvas.ActualHeight));
            runtime.Frame.Width = widget.Width;
            runtime.Frame.Height = widget.Height;
            MoveWidget(runtime, widget.X, widget.Y);
            return oldX != widget.X ||
                   oldY != widget.Y ||
                   oldWidth != widget.Width ||
                   oldHeight != widget.Height;
        }

        private void ShowWidgetLibrary()
        {
            if (WidgetLibraryLayer.Visibility == Visibility.Visible)
                return;

            SetWebViewsVisible(false);
            HideControlOverlayWindow();
            WidgetLibraryLayer.Visibility = Visibility.Visible;
            ResetLibraryPreview();
            RefreshWidgetLibrary();
            LibrarySearchBox.Focus();
            LibrarySearchBox.SelectAll();
        }

        private void HideWidgetLibrary(bool restoreCanvas = true)
        {
            if (WidgetLibraryLayer.Visibility != Visibility.Visible)
                return;

            _librarySearchTimer.Stop();
            ResetLibraryPreview();
            WidgetLibraryLayer.Visibility = Visibility.Collapsed;
            LibrarySearchBox.Clear();
            if (!restoreCanvas)
                return;

            SetWebViewsVisible(true);
            ApplyOverlayWindowVisibility();
        }

        private void RefreshWidgetLibrary()
        {
            if (LibraryItemsPanel == null || LibraryCountText == null)
                return;

            string query = LibrarySearchBox?.Text.Trim() ?? string.Empty;
            List<HtmlWidgetDefinition> matches = _widgets
                .Where(widget => query.Length == 0 ||
                    GetDisplayName(widget).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Reverse()
                .ToList();

            if (_libraryPreviewWidget != null && !matches.Contains(_libraryPreviewWidget))
                ResetLibraryPreview();

            int activeCount = _widgets.Count(widget =>
                widget.Home == HtmlWidgetHome.Canvas && !_detachedWindows.ContainsKey(widget));
            int windowCount = _detachedWindows.Count;
            string matchText = query.Length == 0
                ? $"{_widgets.Count} 个"
                : $"{matches.Count} / {_widgets.Count} 个";
            LibraryCountText.Text = windowCount > 0
                ? $"{matchText} · {activeCount} 个在浮岛 · {windowCount} 个窗口"
                : $"{matchText} · {activeCount} 个在浮岛";
            LibraryItemsPanel.Children.Clear();
            foreach (HtmlWidgetDefinition widget in matches)
                LibraryItemsPanel.Children.Add(CreateLibraryRow(widget));

            LibraryEmptyText.Text = _widgets.Count == 0 ? "还没有组件" : "没有匹配的组件";
            LibraryEmptyText.Visibility = matches.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private Border CreateLibraryRow(HtmlWidgetDefinition widget)
        {
            bool isSelected = _libraryPreviewWidget == widget;
            bool isDetached = _detachedWindows.ContainsKey(widget);
            bool hasError = _runtimes.TryGetValue(widget, out WidgetRuntime? runtime) &&
                            !string.IsNullOrWhiteSpace(runtime.LastError);
            var status = new TextBlock
            {
                Text = "●",
                Foreground = hasError
                    ? new SolidColorBrush(Color.FromRgb(230, 112, 129))
                    : isDetached
                        ? new SolidColorBrush(Color.FromRgb(126, 177, 235))
                    : widget.Home == HtmlWidgetHome.Canvas
                        ? new SolidColorBrush(Color.FromRgb(105, 210, 157))
                        : new SolidColorBrush(Color.FromRgb(88, 106, 132)),
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                ToolTip = hasError
                    ? "组件发生错误"
                    : isDetached ? "正在独立窗口中使用"
                    : widget.Home == HtmlWidgetHome.Canvas ? "正在浮岛中使用" : "已收进组件库"
            };
            var name = new TextBlock
            {
                Text = GetDisplayName(widget),
                Foreground = new SolidColorBrush(Color.FromRgb(232, 239, 250)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var meta = new TextBlock
            {
                Text = $"{widget.Width:0} × {widget.Height:0}  ·  {(hasError ? "异常" : isDetached ? "独立窗口" : widget.Home == HtmlWidgetHome.Canvas ? "在浮岛" : "在组件库")}",
                Foreground = new SolidColorBrush(Color.FromRgb(111, 133, 165)),
                FontSize = 10.5,
                Margin = new Thickness(0, 3, 0, 0)
            };
            var info = new StackPanel();
            info.Children.Add(name);
            info.Children.Add(meta);

            var toggleButton = new Button
            {
                Content = isDetached ? "显示" : widget.Home == HtmlWidgetHome.Canvas ? "收进" : "放回",
                MinWidth = 54,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(8, 0, 2, 0),
                Focusable = false,
                Style = (Style)FindResource(widget.Home == HtmlWidgetHome.Canvas || isDetached
                    ? "ToolButtonStyle"
                    : "AccentButtonStyle")
            };
            toggleButton.Click += (_, _) =>
            {
                if (isDetached)
                    ShowDetachedWidget(widget, owner: null, activate: true, initialPosition: null, hideCanvasAfterOpen: false);
                else if (widget.Home == HtmlWidgetHome.Canvas)
                    ArchiveWidget(widget);
                else
                    RestoreWidget(widget);
            };

            var menuButton = new Button
            {
                Content = "⋯",
                Width = 30,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Focusable = false,
                ToolTip = "更多操作",
                Style = (Style)FindResource("ToolButtonStyle")
            };
            menuButton.Click += (_, _) => OpenLibraryWidgetMenu(widget, menuButton);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(status);
            Grid.SetColumn(info, 1);
            content.Children.Add(info);
            Grid.SetColumn(toggleButton, 2);
            content.Children.Add(toggleButton);
            Grid.SetColumn(menuButton, 3);
            content.Children.Add(menuButton);

            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(11, 9, 8, 9),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(isSelected
                    ? Color.FromArgb(224, 28, 46, 67)
                    : Color.FromArgb(126, 24, 34, 49)),
                BorderBrush = new SolidColorBrush(isSelected
                    ? Color.FromArgb(150, 104, 169, 235)
                    : Color.FromArgb(62, 117, 146, 181)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = content,
                ToolTip = isDetached
                    ? "单击预览 · 右键更多操作"
                    : "单击预览 · 拖动弹出窗口 · 右键更多操作"
            };
            Point dragStart = default;
            bool pointerDown = false;
            bool dragging = false;
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (IsInsideButton(e.OriginalSource as DependencyObject))
                    return;
                pointerDown = true;
                dragging = false;
                dragStart = e.GetPosition(row);
                row.CaptureMouse();
                e.Handled = true;
            };
            row.MouseMove += (_, e) =>
            {
                if (isDetached || !pointerDown || e.LeftButton != MouseButtonState.Pressed)
                    return;
                Point current = e.GetPosition(row);
                if (!dragging &&
                    (Math.Abs(current.X - dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(current.Y - dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance))
                {
                    dragging = true;
                    row.Opacity = 0.58;
                    row.Cursor = Cursors.SizeAll;
                }
                e.Handled = true;
            };
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (!pointerDown)
                    return;
                bool shouldDetach = dragging;
                Point screenPoint = row.PointToScreen(e.GetPosition(row));
                pointerDown = false;
                dragging = false;
                row.Opacity = 1;
                row.Cursor = Cursors.Hand;
                row.ReleaseMouseCapture();
                if (shouldDetach)
                    DetachWidgetFromLibrary(widget, CreateLibraryDetachedPosition(widget, screenPoint));
                else
                    ShowLibraryWidgetPreview(widget);
                e.Handled = true;
            };
            row.LostMouseCapture += (_, _) =>
            {
                pointerDown = false;
                dragging = false;
                row.Opacity = 1;
                row.Cursor = Cursors.Hand;
            };
            row.MouseRightButtonUp += (_, e) =>
            {
                OpenLibraryWidgetMenu(widget, row);
                e.Handled = true;
            };
            return row;
        }

        private static bool IsInsideButton(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is Button)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private string CreateLibraryDetachedPosition(
            HtmlWidgetDefinition widget,
            Point screenPoint)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int width = Math.Max(1, (int)Math.Round(widget.Width * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Round((widget.Height + 37) * dpi.DpiScaleY));
            int left = (int)Math.Round(screenPoint.X - width / 2d);
            int top = (int)Math.Round(screenPoint.Y - 18 * dpi.DpiScaleY);
            return $"{left},{top},{width},{height}";
        }

        private void DetachWidgetFromLibrary(
            HtmlWidgetDefinition widget,
            string? initialPosition)
        {
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? existing))
            {
                existing.ShowWidget();
                return;
            }

            HideWidgetLibrary(restoreCanvas: false);
            SetWebViewsVisible(true);
            ShowDetachedWidget(
                widget,
                owner: null,
                activate: true,
                initialPosition: initialPosition,
                hideCanvasAfterOpen: true,
                returnTarget: HtmlWidgetReturnTarget.Library);
        }

        private async void ShowLibraryWidgetPreview(
            HtmlWidgetDefinition widget,
            bool forceReload = false)
        {
            if (WidgetLibraryLayer.Visibility != Visibility.Visible)
                return;
            if (!forceReload &&
                _libraryPreviewWidget == widget &&
                _libraryPreviewRuntime?.WebView.Visibility == Visibility.Visible)
                return;

            DisposeLibraryPreview();
            _libraryPreviewWidget = widget;
            int generation = ++_libraryPreviewGeneration;
            LibraryPreviewTitleText.Text = GetDisplayName(widget);
            LibraryPreviewMetaText.Text = $"{widget.Width:0} × {widget.Height:0}";
            LibraryPreviewStatusText.Text = "正在初始化";
            LibraryPreviewStatusText.ToolTip = null;
            LibraryPreviewStatusText.Foreground = new SolidColorBrush(Color.FromRgb(113, 133, 165));
            LibraryPreviewEmptyText.Visibility = Visibility.Collapsed;
            LibraryPreviewFrame.Visibility = Visibility.Visible;

            var webView = new WebView2Control
            {
                DefaultBackgroundColor = System.Drawing.Color.Transparent,
                AllowExternalDrop = false,
                Focusable = false,
                IsHitTestVisible = false
            };
            var runtime = new LibraryPreviewRuntime(widget, webView, generation);
            _libraryPreviewRuntime = runtime;
            LibraryPreviewHost.Children.Add(webView);
            UpdateLibraryPreviewSize();
            RefreshWidgetLibrary();

            try
            {
                CoreWebView2Environment environment = await GetWidgetEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(environment);
                if (!IsCurrentLibraryPreview(runtime) || webView.CoreWebView2 == null)
                    return;

                CoreWebView2 core = webView.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.ContextMenuRequested += (_, args) => args.Handled = true;
                core.NewWindowRequested += (_, args) => args.Handled = true;
                core.NavigationStarting += (_, args) =>
                {
                    if (!IsWidgetInternalNavigation(args.Uri))
                        args.Cancel = true;
                };
                core.WebMessageReceived += (_, args) =>
                    HandleWebMessage(
                        widget,
                        webView,
                        args,
                        message => ShowLibraryPreviewScriptError(runtime, message),
                        previewMode: true);
                core.NavigationCompleted += (_, args) =>
                {
                    if (!IsCurrentLibraryPreview(runtime))
                        return;
                    if (args.IsSuccess)
                    {
                        LibraryPreviewStatusText.Text = "实时预览 · 仅查看";
                        LibraryPreviewStatusText.ToolTip = null;
                        LibraryPreviewStatusText.Foreground =
                            new SolidColorBrush(Color.FromRgb(113, 133, 165));
                    }
                    else
                    {
                        ShowLibraryPreviewFatalError(
                            runtime,
                            "预览加载失败：" + args.WebErrorStatus);
                    }
                };
                core.ProcessFailed += (_, args) =>
                    ShowLibraryPreviewFatalError(
                        runtime,
                        "预览进程异常：" + args.ProcessFailedKind);
                await core.AddScriptToExecuteOnDocumentCreatedAsync(WidgetHostBridgeScript);
                if (IsCurrentLibraryPreview(runtime))
                    core.NavigateToString(widget.Html);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                ShowLibraryPreviewFatalError(runtime, "预览初始化失败：" + ex.Message);
            }
        }

        private bool IsCurrentLibraryPreview(LibraryPreviewRuntime runtime) =>
            _libraryPreviewRuntime == runtime &&
            runtime.Generation == _libraryPreviewGeneration &&
            WidgetLibraryLayer.Visibility == Visibility.Visible;

        private void ShowLibraryPreviewScriptError(
            LibraryPreviewRuntime runtime,
            string message)
        {
            if (!IsCurrentLibraryPreview(runtime))
                return;
            LibraryPreviewStatusText.Text = "脚本异常 · " + GetShortWidgetError(message);
            LibraryPreviewStatusText.ToolTip = message;
            LibraryPreviewStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(230, 132, 146));
        }

        private void ShowLibraryPreviewFatalError(
            LibraryPreviewRuntime runtime,
            string message)
        {
            if (!IsCurrentLibraryPreview(runtime))
                return;
            runtime.WebView.Visibility = Visibility.Hidden;
            LibraryPreviewFrame.Visibility = Visibility.Collapsed;
            LibraryPreviewEmptyText.Text = "组件预览加载失败";
            LibraryPreviewEmptyText.Visibility = Visibility.Visible;
            LibraryPreviewStatusText.Text = GetShortWidgetError(message);
            LibraryPreviewStatusText.ToolTip = message;
            LibraryPreviewStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(230, 132, 146));
        }

        private void UpdateLibraryPreviewSize()
        {
            HtmlWidgetDefinition? widget = _libraryPreviewWidget;
            if (widget == null || LibraryPreviewViewport.ActualWidth <= 0 ||
                LibraryPreviewViewport.ActualHeight <= 0)
                return;

            double maxWidth = Math.Max(1, LibraryPreviewViewport.ActualWidth - 36);
            double maxHeight = Math.Max(1, LibraryPreviewViewport.ActualHeight - 36);
            double scale = Math.Min(1,
                Math.Min(maxWidth / Math.Max(1, widget.Width),
                         maxHeight / Math.Max(1, widget.Height)));
            LibraryPreviewFrame.Width = Math.Max(1, widget.Width * scale);
            LibraryPreviewFrame.Height = Math.Max(1, widget.Height * scale);
        }

        private void LibraryPreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateLibraryPreviewSize();

        private void DisposeLibraryPreview()
        {
            _libraryPreviewGeneration++;
            LibraryPreviewRuntime? runtime = _libraryPreviewRuntime;
            _libraryPreviewRuntime = null;
            _libraryPreviewWidget = null;
            if (runtime == null)
                return;

            _composingWebViews.Remove(runtime.WebView);
            LibraryPreviewHost.Children.Clear();
            runtime.WebView.Dispose();
        }

        private void ResetLibraryPreview()
        {
            DisposeLibraryPreview();
            LibraryPreviewHost.Children.Clear();
            LibraryPreviewFrame.Visibility = Visibility.Collapsed;
            LibraryPreviewEmptyText.Text = "选择右侧组件查看";
            LibraryPreviewEmptyText.Visibility = Visibility.Visible;
            LibraryPreviewTitleText.Text = "预览";
            LibraryPreviewMetaText.Text = string.Empty;
            LibraryPreviewStatusText.Text = string.Empty;
            LibraryPreviewStatusText.ToolTip = null;
            LibraryPreviewStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(113, 133, 165));
        }

        private void OpenLibraryWidgetMenu(HtmlWidgetDefinition widget, FrameworkElement target)
        {
            bool isDetached = _detachedWindows.ContainsKey(widget);
            var menu = new ContextMenu
            {
                PlacementTarget = target,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };
            HtmlWidgetMenuTheme.Apply(menu);
            menu.Items.Add(CreateMenuItem(
                isDetached ? "显示独立窗口" : widget.Home == HtmlWidgetHome.Canvas ? "收进组件库" : "放回浮岛",
                () =>
                {
                    if (isDetached)
                        ShowDetachedWidget(widget, owner: null, activate: true, initialPosition: null, hideCanvasAfterOpen: false);
                    else if (widget.Home == HtmlWidgetHome.Canvas)
                        ArchiveWidget(widget);
                    else
                        RestoreWidget(widget);
                }));
            if (!isDetached)
            {
                menu.Items.Add(CreateMenuItem(
                    "弹出为独立窗口",
                    () => DetachWidgetFromLibrary(widget, initialPosition: null)));
            }
            else
            {
                menu.Items.Add(CreateMenuItem(
                    "放回浮岛",
                    () => _detachedWindows[widget].ReturnToCanvas()));
                menu.Items.Add(CreateMenuItem(
                    "收进组件库",
                    () => _detachedWindows[widget].MoveToLibrary()));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("编辑组件…", () => EditWidgetFromLibrary(widget)));
            menu.Items.Add(CreateMenuItem("复制 AI 修改提示词", () => CopyEditPrompt(widget)));
            if (_runtimes.TryGetValue(widget, out WidgetRuntime? runtime))
            {
                MenuItem? diagnosticMenu = CreateDiagnosticMenu(runtime);
                if (diagnosticMenu != null)
                    menu.Items.Add(diagnosticMenu);
            }
            menu.Items.Add(CreateSubmenu(
                "更多操作",
                CreateMenuItem("用剪贴板 HTML 更新", () => UpdateWidgetFromClipboard(widget)),
                CreateMenuItem("复制 HTML", () => CopyText(widget.Html, "HTML 已复制")),
                CreateMenuItem("创建组件副本", () => DuplicateWidget(widget, placeOnCanvas: false))));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateDangerMenuItem(
                "删除组件…",
                () => DeleteWidgetPermanently(widget)));
            menu.IsOpen = true;
        }

        private void EditWidgetFromLibrary(HtmlWidgetDefinition widget)
        {
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? detachedWindow))
            {
                detachedWindow.EditWidget();
                return;
            }
            HideWidgetLibrary(restoreCanvas: false);
            ShowWidgetEditor(widget, returnToLibrary: true);
        }

        private void CloseLibraryButton_Click(object sender, RoutedEventArgs e) => HideWidgetLibrary();

        private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (LibrarySearchHint != null)
            {
                LibrarySearchHint.Visibility = string.IsNullOrEmpty(LibrarySearchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            _librarySearchTimer.Stop();
            if (WidgetLibraryLayer?.Visibility == Visibility.Visible)
                _librarySearchTimer.Start();
        }

        private void WidgetLibraryLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, WidgetLibraryLayer))
            {
                HideWidgetLibrary();
                e.Handled = true;
            }
        }

        private void ShowWidgetEditor(
            HtmlWidgetDefinition? widget,
            bool returnToLibrary = false,
            bool returnToDetached = false,
            Window? detachedOwner = null,
            HtmlWidgetReturnTarget detachedReturnTarget = HtmlWidgetReturnTarget.Canvas)
        {
            _returnToLibraryAfterEditor = returnToLibrary;
            _returnToDetachedAfterEditor = returnToDetached;
            _detachedEditorOwner = returnToDetached ? detachedOwner : null;
            _detachedEditorReturnTarget = detachedReturnTarget;
            _editingWidget = widget;
            if (widget != null)
                _pendingBounds = new Rect(widget.X, widget.Y, widget.Width, widget.Height);
            WidgetEditorTitleText.Text = widget == null ? "添加组件" : "编辑组件";
            WidgetEditorMetaText.Text = $"{_pendingBounds.Width:0} × {_pendingBounds.Height:0}";
            ConfirmEditorButton.Content = widget == null ? "添加" : "保存";
            HtmlEditor.Text = widget?.Html ?? string.Empty;
            SetWebViewsVisible(false);
            HideControlOverlayWindow();
            WidgetEditorLayer.Visibility = Visibility.Visible;
            if (widget == null && !TryLoadClipboardHtml())
                HtmlEditor.Text = HtmlWidgetTemplates.QuickNoteHtml;
            UpdateHtmlValidation();
            HtmlEditor.Focus();
        }

        private void HideWidgetEditor()
        {
            bool returnToLibrary = _returnToLibraryAfterEditor;
            bool returnToDetached = _returnToDetachedAfterEditor;
            Window? detachedOwner = _detachedEditorOwner;
            HtmlWidgetReturnTarget detachedReturnTarget = _detachedEditorReturnTarget;
            HtmlWidgetDefinition? editingWidget = _editingWidget;
            _returnToLibraryAfterEditor = false;
            _returnToDetachedAfterEditor = false;
            _detachedEditorOwner = null;
            WidgetEditorLayer.Visibility = Visibility.Collapsed;
            _editingWidget = null;
            HtmlEditor.Clear();
            if (returnToLibrary)
            {
                ShowWidgetLibrary();
                return;
            }
            if (returnToDetached && editingWidget != null)
            {
                ShowDetachedWidget(
                    editingWidget,
                    owner: detachedOwner,
                    activate: true,
                    initialPosition: editingWidget.DetachedPosition,
                    hideCanvasAfterOpen: true,
                    returnTarget: detachedReturnTarget);
                return;
            }
            SetWebViewsVisible(true);
            ApplyOverlayWindowVisibility();
        }

        private void ConfirmEditorButton_Click(object sender, RoutedEventArgs e) => ConfirmWidgetEditor();

        private void ConfirmWidgetEditor()
        {
            string html = ExtractHtml(HtmlEditor.Text);
            if (!IsValidHtmlDocument(html))
            {
                ShowToast("请提供完整的单文件 HTML");
                return;
            }
            if (Encoding.UTF8.GetByteCount(html) > MaximumWidgetHtmlBytes)
            {
                ShowToast("组件 HTML 不能超过 2 MB");
                return;
            }

            HtmlWidgetDefinition? editingWidget = _editingWidget;
            bool isNew = editingWidget == null;
            HtmlWidgetDefinition? addedWidget = null;
            string? previousHtml = editingWidget?.Html;
            if (editingWidget == null)
            {
                AddWidget(html, _pendingBounds);
                addedWidget = _widgets[^1];
            }
            else
            {
                html = EnsureUniqueWidgetTitle(html, editingWidget);
                editingWidget.Html = html;
                MarkDirty();
            }

            bool saved = SaveNow();
            if (!saved)
            {
                if (addedWidget != null)
                {
                    RemoveWidget(addedWidget);
                    RefreshWidgetLibrary();
                }
                else if (editingWidget != null && previousHtml != null)
                {
                    editingWidget.Html = previousHtml;
                }
                MarkDirty();
                HtmlValidationText.Text = "保存失败，请检查数据目录后重试";
                HtmlValidationText.ToolTip = _lastSaveError;
                HtmlValidationText.Foreground =
                    new SolidColorBrush(Color.FromRgb(230, 132, 146));
                ConfirmEditorButton.IsEnabled = true;
                return;
            }

            if (editingWidget != null)
                ReloadWidget(editingWidget);
            HideWidgetEditor();
            ShowToast(isNew ? "组件已添加" : "修改已保存");
        }

        private async void ReloadWidget(HtmlWidgetDefinition widget)
        {
            if (_detachedWindows.TryGetValue(widget, out HtmlWidgetWindow? detachedWindow))
            {
                detachedWindow.Reload();
                return;
            }
            if (!_runtimes.TryGetValue(widget, out WidgetRuntime? oldRuntime))
                return;
            int zIndex = Canvas.GetZIndex(oldRuntime.Frame);
            await PrepareCanvasRuntimeForDisposalAsync(widget);
            if (!_runtimes.TryGetValue(widget, out WidgetRuntime? currentRuntime) ||
                currentRuntime != oldRuntime ||
                widget.Home != HtmlWidgetHome.Canvas ||
                _detachedWindows.ContainsKey(widget))
            {
                return;
            }
            RemoveWidgetFrame(widget);
            AddWidgetFrame(widget);
            if (_runtimes.TryGetValue(widget, out WidgetRuntime? newRuntime))
                Canvas.SetZIndex(newRuntime.Frame, zIndex);
        }

        private void CancelEditorButton_Click(object sender, RoutedEventArgs e) => HideWidgetEditor();

        private void HtmlEditor_TextChanged(object sender, TextChangedEventArgs e) => UpdateHtmlValidation();

        private void UpdateHtmlValidation()
        {
            if (HtmlValidationText == null || HtmlEditor == null || ConfirmEditorButton == null)
                return;
            HtmlValidationText.ToolTip = null;
            string html = ExtractHtml(HtmlEditor.Text);
            var errors = new List<string>();
            string lower = html.ToLowerInvariant();
            if (html.Length == 0)
            {
                HtmlValidationText.Text = "请粘贴完整单文件 HTML";
                HtmlValidationText.Foreground = new SolidColorBrush(Color.FromRgb(113, 133, 165));
                ConfirmEditorButton.IsEnabled = false;
                return;
            }
            if (!lower.Contains("<!doctype html", StringComparison.Ordinal))
                errors.Add("doctype");
            if (!lower.Contains("<html", StringComparison.Ordinal))
                errors.Add("html");
            if (string.IsNullOrWhiteSpace(GetHtmlTitle(html)))
                errors.Add("有效 title");
            if (!lower.Contains("<body", StringComparison.Ordinal))
                errors.Add("body");
            if (!lower.Contains("<style", StringComparison.Ordinal))
                errors.Add("style");
            bool hasExternalResources =
                lower.Contains("src=\"http", StringComparison.Ordinal) ||
                lower.Contains("src='http", StringComparison.Ordinal) ||
                lower.Contains("href=\"http", StringComparison.Ordinal) ||
                lower.Contains("href='http", StringComparison.Ordinal) ||
                lower.Contains("@import", StringComparison.Ordinal);
            bool tooLarge = Encoding.UTF8.GetByteCount(html) > MaximumWidgetHtmlBytes;
            bool valid = errors.Count == 0 && !tooLarge;
            ConfirmEditorButton.IsEnabled = valid;
            if (!valid)
            {
                HtmlValidationText.Foreground = new SolidColorBrush(Color.FromRgb(255, 172, 128));
                HtmlValidationText.Text = tooLarge
                    ? "超过 2 MB，无法加载"
                    : "缺少 " + string.Join("、", errors);
            }
            else if (hasExternalResources)
            {
                HtmlValidationText.Foreground = new SolidColorBrush(Color.FromRgb(241, 194, 115));
                HtmlValidationText.Text = "可以保存 · 使用了外部资源";
            }
            else
            {
                HtmlValidationText.Foreground = new SolidColorBrush(Color.FromRgb(120, 214, 162));
                HtmlValidationText.Text = _editingWidget == null ? "✓ 可以添加" : "✓ 可以保存";
            }
        }

        private static bool IsValidHtmlDocument(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;
            return html.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase) &&
                   html.Contains("<html", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(GetHtmlTitle(html)) &&
                   html.Contains("<body", StringComparison.OrdinalIgnoreCase) &&
                   html.Contains("<style", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryLoadClipboardHtml()
        {
            try
            {
                if (!Clipboard.ContainsText())
                    return false;

                string html = ExtractHtml(Clipboard.GetText());
                if (!IsValidHtmlDocument(html))
                    return false;

                HtmlEditor.Text = html;
                return true;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                return false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => DisposeWindow();

        private void CopyGeneralPromptButton_Click(object sender, RoutedEventArgs e)
        {
            string prompt = HtmlWidgetCanvasPrompt.BuildCreateTemplate();
            if (CopyText(prompt, "提示词已复制，在末尾补充需求即可"))
                _ = Dispatcher.BeginInvoke(HideWindow);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (WidgetEditorLayer.Visibility == Visibility.Visible &&
                e.Key == Key.Enter &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (ConfirmEditorButton.IsEnabled)
                    ConfirmWidgetEditor();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape)
                return;
            if (WidgetEditorLayer.Visibility == Visibility.Visible)
                HideWidgetEditor();
            else if (WidgetLibraryLayer.Visibility == Visibility.Visible)
                HideWidgetLibrary();
            else
                HideWindow();
            e.Handled = true;
        }

        private async Task InitializeWebViewAsync(WidgetRuntime runtime)
        {
            string stage = "创建运行环境";
            try
            {
                CoreWebView2Environment environment = await GetWidgetEnvironmentAsync();
                stage = "创建浏览器控制器";
                // WebView2 控制器建立前后都不要通过 Win32 改变主窗口的 Parent、
                // 顶级/子窗口 Style 或原生 Z-order。普通 WPF Topmost 不在此限制内。
                await runtime.WebView.EnsureCoreWebView2Async(environment);
                if (!_runtimes.ContainsKey(runtime.Definition) || runtime.WebView.CoreWebView2 == null)
                    return;

                stage = "配置浏览器";
                CoreWebView2 core = runtime.WebView.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.ContextMenuRequested += (_, args) =>
                {
                    args.Handled = true;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        FrameworkElement target = (FrameworkElement?)runtime.DragStrip
                            ?? runtime.Frame;
                        OpenWidgetMenu(runtime, target);
                    });
                };
                core.NewWindowRequested += (_, args) =>
                {
                    // WebView2 可能在页面初始化期间以 about:blank 触发此事件。
                    // 不得转交系统浏览器；外部打开只能通过 widgetHost 显式请求。
                    args.Handled = true;
                    Debug.WriteLine("HtmlWidgetCanvasWindow 已拦截组件新窗口：" + args.Uri);
                };
                core.NavigationStarting += (_, args) =>
                {
                    if (IsWidgetInternalNavigation(args.Uri))
                        return;
                    args.Cancel = true;
                    Debug.WriteLine("HtmlWidgetCanvasWindow 已拦截组件页面导航：" + args.Uri);
                };
                core.WebMessageReceived += (_, args) =>
                    HandleWebMessage(runtime.Definition, runtime.WebView, args);
                core.NavigationCompleted += (_, args) =>
                {
                    // 页面启动脚本可能在 NavigationCompleted 之前上报错误；成功导航只能
                    // 清理由导航或浏览器本身造成的致命状态，不能抹掉组件脚本异常。
                    if (args.IsSuccess)
                    {
                        if (runtime.HasFatalError)
                            ClearWidgetError(runtime);
                    }
                    else
                        ShowFatalWidgetError(runtime, "页面加载失败：" + args.WebErrorStatus);
                };
                core.ProcessFailed += (_, args) =>
                    ShowFatalWidgetError(runtime, "浏览器进程异常：" + args.ProcessFailedKind);
                stage = "注入宿主脚本";
                await core.AddScriptToExecuteOnDocumentCreatedAsync(WidgetHostBridgeScript);
                stage = "加载组件 HTML";
                core.NavigateToString(runtime.Definition.Html);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                string detail = $"WebView2 {stage}失败：{ex.Message}";
                Debug.WriteLine(detail + Environment.NewLine + ex);
                string diagnostics = $"{detail}{Environment.NewLine}" +
                                     $"HRESULT: 0x{ex.HResult:X8}{Environment.NewLine}{Environment.NewLine}{ex}";
                ShowFatalWidgetError(runtime, detail, diagnostics);
            }
        }

        internal static Task<CoreWebView2Environment> GetWidgetEnvironmentAsync()
        {
            if (_environmentTask is { IsFaulted: true } or { IsCanceled: true })
                _environmentTask = null;
            if (_environmentTask != null)
                return _environmentTask;

            string userDataFolder = AppPaths.WebView2DataFolder;
            Directory.CreateDirectory(userDataFolder);
            return _environmentTask = CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);
        }

        private async void HandleWebMessage(
            HtmlWidgetDefinition widget,
            WebView2Control webView,
            CoreWebView2WebMessageReceivedEventArgs args,
            Action<string>? runtimeErrorHandler = null,
            Action<string>? hostErrorHandler = null,
            bool previewMode = false,
            Action? hideAction = null,
            Window? dialogOwner = null)
        {
            string requestId = string.Empty;
            string method = string.Empty;
            try
            {
                using JsonDocument document = JsonDocument.Parse(args.WebMessageAsJson);
                JsonElement root = document.RootElement;
                if (string.Equals(GetString(root, "type"), "compositionState", StringComparison.Ordinal))
                {
                    bool active = root.TryGetProperty("active", out JsonElement activeElement) &&
                                  activeElement.ValueKind == JsonValueKind.True;
                    if (active)
                        _composingWebViews.Add(webView);
                    else
                        _composingWebViews.Remove(webView);
                    if (dialogOwner is HtmlWidgetWindow detachedWindow)
                        detachedWindow.HandleCompositionStateChanged(active);
                    return;
                }

                if (string.Equals(GetString(root, "type"), "runtimeError", StringComparison.Ordinal))
                {
                    string message = GetString(root, "message");
                    string stack = GetString(root, "stack");
                    string error = string.IsNullOrWhiteSpace(stack)
                        ? message
                        : message + Environment.NewLine + Environment.NewLine + stack;
                    if (runtimeErrorHandler != null)
                        runtimeErrorHandler(error);
                    else if (_runtimes.TryGetValue(widget, out WidgetRuntime? runtime) &&
                             runtime.WebView == webView)
                        MarkWidgetRuntimeError(runtime, error);
                    return;
                }

                requestId = GetString(root, "id");
                method = GetString(root, "method");
                JsonElement payload = root.TryGetProperty("args", out JsonElement value)
                    ? value
                    : default;
                object? result = previewMode
                    ? await DispatchWidgetPreviewRequestAsync(widget, method, payload)
                    : await DispatchWidgetRequestAsync(
                        widget,
                        method,
                        payload,
                        hideAction,
                        dialogOwner ?? this);
                Reply(webView, requestId, true, result, null);
            }
            catch (Exception ex)
            {
                string detail = string.IsNullOrWhiteSpace(method)
                    ? ex.Message
                    : method + "：" + ex.Message;
                Debug.WriteLine("HtmlWidgetCanvasWindow 宿主调用失败：" + detail);
                if (hostErrorHandler != null)
                    hostErrorHandler(detail);
                else if (_runtimes.TryGetValue(widget, out WidgetRuntime? runtime) &&
                         runtime.WebView == webView)
                    runtime.LastHostError = detail;
                Reply(webView, requestId, false, null, ex.Message);
            }
        }

        internal void HandleDetachedWebMessage(
            HtmlWidgetDefinition widget,
            WebView2Control webView,
            CoreWebView2WebMessageReceivedEventArgs args,
            Action<string> runtimeErrorHandler,
            Action<string> hostErrorHandler,
            Action hideAction,
            Window dialogOwner) =>
            HandleWebMessage(
                widget,
                webView,
                args,
                runtimeErrorHandler: runtimeErrorHandler,
                hostErrorHandler: hostErrorHandler,
                previewMode: false,
                hideAction: hideAction,
                dialogOwner: dialogOwner);

        private Task<object?> DispatchWidgetPreviewRequestAsync(
            HtmlWidgetDefinition widget,
            string method,
            JsonElement args)
        {
            // 组件库预览只负责真实渲染，不应因为页面初始化而改变状态、
            // 写剪贴板、打开外部目标、隐藏窗口或弹出选择对话框。
            return method switch
            {
                "state.write" or "state.flush" or
                "clipboard.write" or
                "url.open" or "path.open" or
                "window.hide" => Task.FromResult<object?>(true),
                "process.start" => Task.FromResult<object?>(new
                {
                    started = true,
                    processId = (int?)null
                }),
                "process.run" => Task.FromResult<object?>(new
                {
                    exitCode = (int?)0,
                    stdout = string.Empty,
                    stderr = string.Empty,
                    timedOut = false,
                    truncated = false
                }),
                "state.remove" => Task.FromResult<object?>(false),
                "state.clear" => Task.FromResult<object?>(0),
                "fs.selectFile" or "fs.selectFolder" =>
                    Task.FromResult<object?>(null),
                _ => DispatchWidgetRequestAsync(
                    widget,
                    method,
                    args,
                    hideAction: null,
                    dialogOwner: this)
            };
        }

        private async Task<object?> DispatchWidgetRequestAsync(
            HtmlWidgetDefinition widget,
            string method,
            JsonElement args,
            Action? hideAction,
            Window dialogOwner)
        {
            switch (method)
            {
                case "state.read":
                {
                    string key = GetRequiredString(args, "key");
                    if (widget.State.TryGetValue(key, out JsonElement value))
                        return value;
                    return args.ValueKind == JsonValueKind.Object &&
                           args.TryGetProperty("defaultValue", out JsonElement fallback)
                        ? fallback.Clone()
                        : null;
                }
                case "state.write":
                {
                    string key = GetRequiredString(args, "key");
                    if (args.ValueKind != JsonValueKind.Object ||
                        !args.TryGetProperty("value", out JsonElement value))
                    {
                        throw new ArgumentException("state.write 缺少参数：value");
                    }
                    widget.State[key] = value.Clone();
                    MarkDirty();
                    return true;
                }
                case "state.remove":
                {
                    bool removed = widget.State.Remove(GetRequiredString(args, "key"));
                    if (removed)
                        MarkDirty();
                    return removed;
                }
                case "state.clear":
                {
                    int count = widget.State.Count;
                    if (count > 0)
                    {
                        widget.State.Clear();
                        MarkDirty();
                    }
                    return count;
                }
                case "state.flush":
                    if (!SaveNow())
                        throw new IOException("组件状态写入数据文件失败");
                    return true;
                case "clipboard.read":
                    return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                case "clipboard.write":
                {
                    string text = GetString(args, "text");
                    if (text.Length == 0)
                        Clipboard.Clear();
                    else
                        Clipboard.SetText(text);
                    return true;
                }
                case "url.open":
                {
                    string url = GetRequiredString(args, "url");
                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        throw new ArgumentException("url.open 只支持 http/https 地址");
                    }
                    OpenExternal(url, reportFailure: true);
                    if (GetBoolean(args, "hideAfterOpen"))
                        _ = Dispatcher.BeginInvoke(hideAction ?? HideWindow);
                    return true;
                }
                case "path.open":
                {
                    string path = NormalizeFilePath(GetRequiredString(args, "path"));
                    if (!File.Exists(path) && !Directory.Exists(path))
                        throw new FileNotFoundException("path.open 指定的路径不存在", path);
                    OpenExternal(path, reportFailure: true);
                    if (GetBoolean(args, "hideAfterOpen"))
                        _ = Dispatcher.BeginInvoke(hideAction ?? HideWindow);
                    return true;
                }
                case "window.hide":
                    _ = Dispatcher.BeginInvoke(hideAction ?? HideWindow);
                    return true;
                case "process.start":
                    return StartWidgetProcess(args, hideAction);
                case "process.run":
                    return await RunWidgetProcessAsync(args);
                case "http.get":
                case "http.post":
                case "http.request":
                    return await HandleHttpRequestAsync(method, args);
                case "fs.exists":
                case "fs.getKnownFolders":
                case "fs.getInfo":
                case "fs.readText":
                case "fs.readBase64":
                case "fs.list":
                case "fs.selectFile":
                case "fs.selectFolder":
                    return await HandleFileRequestAsync(method, args, dialogOwner);
                default:
                    throw new NotSupportedException("未支持的宿主能力：" + method);
            }
        }

        private static async Task<object> HandleHttpRequestAsync(string bridgeMethod, JsonElement args)
        {
            string url = GetRequiredString(args, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("只支持 http/https 地址");
            }

            string method = bridgeMethod switch
            {
                "http.get" => "GET",
                "http.post" => "POST",
                _ => GetString(args, "method")
            };
            if (string.IsNullOrWhiteSpace(method))
                method = "GET";

            using var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);
            request.Headers.UserAgent.ParseAdd("DesktopWidgetHost/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            string body = GetString(args, "body");
            if (body.Length > 0 || method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
            {
                string contentType = GetString(args, "contentType");
                if (string.IsNullOrWhiteSpace(contentType))
                    contentType = "application/json";
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            if (args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty("headers", out JsonElement headers) &&
                headers.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty header in headers.EnumerateObject())
                {
                    string value = header.Value.ValueKind == JsonValueKind.String
                        ? header.Value.GetString() ?? string.Empty
                        : header.Value.ToString();
                    if (!request.Headers.TryAddWithoutValidation(header.Name, value) && request.Content != null)
                        request.Content.Headers.TryAddWithoutValidation(header.Name, value);
                }
            }

            int timeoutMs = GetInteger(args, "timeoutMs", 15000, 1000, 120000);
            int maxBytes = GetInteger(args, "maxBytes", 2 * 1024 * 1024, 1024, 10 * 1024 * 1024);
            using var cancellation = new CancellationTokenSource(timeoutMs);
            using HttpResponseMessage response = await SendHttpRequestAsync(
                request, cancellation, timeoutMs);

            byte[] bytes = await ReadStreamLimitedAsync(
                await response.Content.ReadAsStreamAsync(cancellation.Token),
                maxBytes,
                cancellation.Token);
            bool truncated = bytes.Length > maxBytes;
            if (truncated)
                Array.Resize(ref bytes, maxBytes);
            string text = DecodeResponseText(bytes, response.Content.Headers.ContentType?.CharSet);
            var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
                responseHeaders[header.Key] = string.Join(", ", header.Value);

            return new
            {
                ok = response.IsSuccessStatusCode,
                status = (int)response.StatusCode,
                statusText = response.ReasonPhrase ?? string.Empty,
                text,
                contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url,
                headers = responseHeaders,
                truncated
            };
        }

        private static async Task<HttpResponseMessage> SendHttpRequestAsync(
            HttpRequestMessage request,
            CancellationTokenSource cancellation,
            int timeoutMs)
        {
            try
            {
                return await SharedHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token);
            }
            catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
            {
                throw new TimeoutException($"HTTP 请求超时（{timeoutMs} 毫秒）", ex);
            }
        }

        private async Task<object?> HandleFileRequestAsync(
            string method,
            JsonElement args,
            Window dialogOwner)
        {
            if (method == "fs.getKnownFolders")
                return GetKnownFolders();

            if (method == "fs.selectFile")
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = GetString(args, "title"),
                    Filter = string.IsNullOrWhiteSpace(GetString(args, "filter"))
                        ? "所有文件|*.*"
                        : GetString(args, "filter"),
                    DefaultExt = GetString(args, "defaultExtension"),
                    FileName = GetString(args, "defaultFileName"),
                    InitialDirectory = GetString(args, "initialDirectory"),
                    CheckFileExists = true,
                    Multiselect = false
                };
                bool selected = dialogOwner is { IsLoaded: true, IsVisible: true }
                    ? dialog.ShowDialog(dialogOwner) == true
                    : dialog.ShowDialog() == true;
                return selected
                    ? BuildFileInfo(dialog.FileName)
                    : null;
            }

            if (method == "fs.selectFolder")
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = GetString(args, "title"),
                    InitialDirectory = GetString(args, "initialDirectory"),
                    Multiselect = false
                };
                bool selected = dialogOwner is { IsLoaded: true, IsVisible: true }
                    ? dialog.ShowDialog(dialogOwner) == true
                    : dialog.ShowDialog() == true;
                return selected
                    ? BuildFileInfo(dialog.FolderName)
                    : null;
            }

            string inputPath = GetRequiredString(args, "path");
            return await Task.Run<object>(() =>
            {
                string path = NormalizeFilePath(inputPath);
                switch (method)
                {
                    case "fs.exists":
                    {
                        bool isFile = File.Exists(path);
                        bool isFolder = Directory.Exists(path);
                        string? type = isFile ? "file" : isFolder ? "folder" : null;
                        return new
                        {
                            exists = isFile || isFolder,
                            type,
                            path
                        };
                    }
                    case "fs.getInfo":
                        return BuildFileInfo(path);
                    case "fs.readText":
                    {
                        EnsureFileExists(path);
                        int maxBytes = GetInteger(args, "maxBytes", 1024 * 1024, 1024, 5 * 1024 * 1024);
                        byte[] bytes = ReadFileLimited(path, maxBytes, out bool truncated);
                        (string text, string encoding) = DecodeText(
                            bytes,
                            GetString(args, "encoding"));
                        return new
                        {
                            path,
                            name = Path.GetFileName(path),
                            extension = Path.GetExtension(path),
                            encoding,
                            text,
                            truncated
                        };
                    }
                    case "fs.readBase64":
                    {
                        EnsureFileExists(path);
                        int maxBytes = GetInteger(args, "maxBytes", 5 * 1024 * 1024, 1024, 20 * 1024 * 1024);
                        byte[] bytes = ReadFileLimited(path, maxBytes, out bool truncated);
                        return new
                        {
                            path,
                            name = Path.GetFileName(path),
                            extension = Path.GetExtension(path),
                            mime = GetMimeType(path),
                            base64 = Convert.ToBase64String(bytes),
                            truncated
                        };
                    }
                    case "fs.list":
                        return BuildFileList(path, args);
                    default:
                        throw new NotSupportedException("未支持的文件能力：" + method);
                }
            });
        }

        private static object BuildFileList(string path, JsonElement args)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("文件夹不存在：" + path);
            string pattern = GetString(args, "pattern");
            if (string.IsNullOrWhiteSpace(pattern))
                pattern = "*";
            bool recursive = GetBoolean(args, "recursive");
            bool includeFiles = GetBoolean(args, "includeFiles", true);
            bool includeFolders = GetBoolean(args, "includeFolders", true);
            bool includeHidden = GetBoolean(args, "includeHidden");
            int maxItems = GetInteger(args, "maxItems", 200, 1, 1000);
            var items = new List<Dictionary<string, object?>>();
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            bool truncated = false;

            if (includeFolders)
            {
                foreach (string folder in Directory.EnumerateDirectories(path, "*", option))
                {
                    if (!includeHidden && IsHidden(folder))
                        continue;
                    if (items.Count >= maxItems)
                    {
                        truncated = true;
                        break;
                    }
                    items.Add(BuildFileInfo(folder));
                }
            }
            if (includeFiles && !truncated)
            {
                foreach (string file in Directory.EnumerateFiles(path, pattern, option))
                {
                    if (!includeHidden && IsHidden(file))
                        continue;
                    if (items.Count >= maxItems)
                    {
                        truncated = true;
                        break;
                    }
                    items.Add(BuildFileInfo(file));
                }
            }
            return new { path, count = items.Count, recursive, pattern, truncated, items };
        }

        private static Dictionary<string, object?> BuildFileInfo(string path)
        {
            string fullPath = NormalizeFilePath(path);
            bool isFile = File.Exists(fullPath);
            bool isFolder = Directory.Exists(fullPath);
            if (!isFile && !isFolder)
                throw new FileNotFoundException("路径不存在", fullPath);
            FileSystemInfo info = isFile ? new FileInfo(fullPath) : new DirectoryInfo(fullPath);
            var result = new Dictionary<string, object?>
            {
                ["type"] = isFile ? "file" : "folder",
                ["path"] = fullPath,
                ["name"] = info.Name,
                ["extension"] = isFile ? Path.GetExtension(fullPath) : string.Empty,
                ["size"] = isFile ? ((FileInfo)info).Length : 0L,
                ["createdAt"] = info.CreationTime.ToString("O"),
                ["modifiedAt"] = info.LastWriteTime.ToString("O")
            };
            return result;
        }

        private static async Task<byte[]> ReadStreamLimitedAsync(
            Stream stream,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream(Math.Min(maxBytes + 1, 64 * 1024));
            byte[] buffer = new byte[16 * 1024];
            while (memory.Length <= maxBytes)
            {
                int requested = (int)Math.Min(buffer.Length, maxBytes + 1L - memory.Length);
                int read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                    break;
                memory.Write(buffer, 0, read);
            }
            return memory.ToArray();
        }

        private static string DecodeResponseText(byte[] bytes, string? charset)
        {
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try
                {
                    return DecodeUsing(
                        bytes,
                        Encoding.GetEncoding(charset.Trim('"')));
                }
                catch (ArgumentException)
                {
                }
            }
            return DecodeText(bytes).Text;
        }

        private static (string Text, string EncodingName) DecodeText(
            byte[] bytes,
            string? requestedEncoding = null)
        {
            if (!string.IsNullOrWhiteSpace(requestedEncoding))
            {
                try
                {
                    Encoding encoding = Encoding.GetEncoding(requestedEncoding.Trim());
                    return (DecodeUsing(bytes, encoding), encoding.WebName);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException("不支持的文本编码：" + requestedEncoding, ex);
                }
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE &&
                bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                return (Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4), "utf-32-le");
            }
            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 &&
                bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                var utf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                return (utf32Be.GetString(bytes, 4, bytes.Length - 4), "utf-32-be");
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16-le");
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16-be");

            try
            {
                var strictUtf8 = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true);
                return (strictUtf8.GetString(bytes), "utf-8");
            }
            catch (DecoderFallbackException)
            {
                Encoding gb18030 = Encoding.GetEncoding("gb18030");
                return (gb18030.GetString(bytes), gb18030.WebName);
            }
        }

        private static string DecodeUsing(byte[] bytes, Encoding encoding)
        {
            byte[] preamble = encoding.GetPreamble();
            int offset = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)
                ? preamble.Length
                : 0;
            return encoding.GetString(bytes, offset, bytes.Length - offset);
        }

        private static byte[] ReadFileLimited(string path, int maxBytes, out bool truncated)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int targetLength = (int)Math.Min(stream.Length, maxBytes + 1L);
            byte[] bytes = new byte[targetLength];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                    break;
                offset += read;
            }
            if (offset != bytes.Length)
                Array.Resize(ref bytes, offset);
            truncated = bytes.Length > maxBytes || stream.Length > maxBytes;
            if (bytes.Length > maxBytes)
                Array.Resize(ref bytes, maxBytes);
            return bytes;
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空");
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        }

        private static object GetKnownFolders()
        {
            string userProfile = GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return new
            {
                userProfile,
                desktop = GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Path.Combine(userProfile, "Desktop")),
                documents = GetFolderPath(Environment.SpecialFolder.MyDocuments, Path.Combine(userProfile, "Documents")),
                downloads = GetKnownFolderPath(
                    new Guid("374DE290-123F-4565-9164-39C4925E467B"),
                    Path.Combine(userProfile, "Downloads")),
                pictures = GetFolderPath(Environment.SpecialFolder.MyPictures, Path.Combine(userProfile, "Pictures")),
                music = GetFolderPath(Environment.SpecialFolder.MyMusic, Path.Combine(userProfile, "Music")),
                videos = GetFolderPath(Environment.SpecialFolder.MyVideos, Path.Combine(userProfile, "Videos")),
                appData = GetFolderPath(Environment.SpecialFolder.ApplicationData, Path.Combine(userProfile, "AppData", "Roaming")),
                localAppData = GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Path.Combine(userProfile, "AppData", "Local")),
                temp = Path.GetFullPath(Path.GetTempPath())
            };
        }

        private static string GetFolderPath(Environment.SpecialFolder folder, string fallback)
        {
            string path = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(path) ? Path.GetFullPath(fallback) : Path.GetFullPath(path);
        }

        private static string GetKnownFolderPath(Guid folderId, string fallback)
        {
            IntPtr pathPointer = IntPtr.Zero;
            try
            {
                Guid id = folderId;
                int result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pathPointer);
                string? path = result == 0 && pathPointer != IntPtr.Zero
                    ? Marshal.PtrToStringUni(pathPointer)
                    : null;
                return string.IsNullOrWhiteSpace(path) ? Path.GetFullPath(fallback) : Path.GetFullPath(path);
            }
            finally
            {
                if (pathPointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        private object StartWidgetProcess(JsonElement args, Action? hideAction)
        {
            ProcessStartInfo startInfo = CreateWidgetProcessStartInfo(args, "process.start");
            string windowStyleText = GetString(args, "windowStyle");
            if (string.IsNullOrWhiteSpace(windowStyleText))
                windowStyleText = "normal";
            ProcessWindowStyle windowStyle = windowStyleText.Trim().ToLowerInvariant() switch
            {
                "normal" => ProcessWindowStyle.Normal,
                "hidden" => ProcessWindowStyle.Hidden,
                "minimized" => ProcessWindowStyle.Minimized,
                "maximized" => ProcessWindowStyle.Maximized,
                _ => throw new ArgumentException("windowStyle 只支持 normal、hidden、minimized 或 maximized")
            };

            startInfo.WindowStyle = windowStyle;
            startInfo.CreateNoWindow = windowStyle == ProcessWindowStyle.Hidden;

            int processId;
            using (Process process = StartWidgetProcessCore(startInfo))
                processId = process.Id;
            if (GetBoolean(args, "hideAfterStart"))
                _ = Dispatcher.BeginInvoke(hideAction ?? HideWindow);
            return new { started = true, processId };
        }

        private static async Task<object> RunWidgetProcessAsync(JsonElement args)
        {
            ProcessStartInfo startInfo = CreateWidgetProcessStartInfo(args, "process.run");
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardInputEncoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false);

            string input = GetString(args, "input");
            if (Encoding.UTF8.GetByteCount(input) > MaximumProcessInputBytes)
                throw new ArgumentException("process.run 的 input 不能超过 1 MB");
            int timeoutMs = GetInteger(args, "timeoutMs", 15000, 100, 10 * 60 * 1000);
            int maxOutputBytes = GetInteger(
                args,
                "maxOutputBytes",
                1024 * 1024,
                1024,
                10 * 1024 * 1024);

            using Process process = StartWidgetProcessCore(startInfo);
            var budget = new ProcessOutputBudget(maxOutputBytes);
            Task<byte[]> stdoutTask = ReadProcessOutputAsync(
                process.StandardOutput.BaseStream,
                budget);
            Task<byte[]> stderrTask = ReadProcessOutputAsync(
                process.StandardError.BaseStream,
                budget);
            Task inputTask = WriteProcessInputAsync(process, input);
            Task exitTask = process.WaitForExitAsync();
            bool timedOut = false;
            try
            {
                await exitTask
                    .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
                catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
                {
                    throw new InvalidOperationException("进程超时且无法终止：" + ex.Message, ex);
                }
                await exitTask.ConfigureAwait(false);
            }

            await inputTask.ConfigureAwait(false);
            byte[][] output = await Task
                .WhenAll(stdoutTask, stderrTask)
                .ConfigureAwait(false);
            string stdout = DecodeText(output[0]).Text;
            string stderr = DecodeText(output[1]).Text;
            int? exitCode = timedOut ? null : process.ExitCode;
            return new
            {
                exitCode,
                stdout,
                stderr,
                timedOut,
                truncated = budget.Truncated
            };
        }

        private static ProcessStartInfo CreateWidgetProcessStartInfo(
            JsonElement args,
            string method)
        {
            string file = Environment.ExpandEnvironmentVariables(
                GetRequiredString(args, "file").Trim());
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentException(method + " 的 file 不能为空");

            var startInfo = new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = false
            };
            string workingDirectory = GetString(args, "workingDirectory");
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = NormalizeFilePath(workingDirectory);
                if (!Directory.Exists(workingDirectory))
                    throw new DirectoryNotFoundException("工作目录不存在：" + workingDirectory);
                startInfo.WorkingDirectory = workingDirectory;
            }

            if (args.ValueKind != JsonValueKind.Object ||
                !args.TryGetProperty("args", out JsonElement argumentList) ||
                argumentList.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return startInfo;
            }
            if (argumentList.ValueKind != JsonValueKind.Array)
                throw new ArgumentException(method + " 的 args 必须是字符串数组");
            foreach (JsonElement argument in argumentList.EnumerateArray())
            {
                if (argument.ValueKind != JsonValueKind.String)
                    throw new ArgumentException(method + " 的 args 只能包含字符串");
                startInfo.ArgumentList.Add(argument.GetString() ?? string.Empty);
            }
            return startInfo;
        }

        private static Process StartWidgetProcessCore(ProcessStartInfo startInfo)
        {
            try
            {
                return Process.Start(startInfo) ??
                    throw new InvalidOperationException("系统未能创建进程");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                throw new InvalidOperationException("启动本地进程失败：" + ex.Message, ex);
            }
        }

        private static async Task WriteProcessInputAsync(Process process, string input)
        {
            try
            {
                if (input.Length > 0)
                {
                    await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                }
            }
        }

        private static async Task<byte[]> ReadProcessOutputAsync(
            Stream stream,
            ProcessOutputBudget budget)
        {
            using var memory = new MemoryStream(64 * 1024);
            byte[] buffer = new byte[16 * 1024];
            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    int keep = budget.Reserve(read);
                    if (keep > 0)
                        memory.Write(buffer, 0, keep);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
            return memory.ToArray();
        }

        private static void EnsureFileExists(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("文件不存在", path);
        }

        private static bool IsHidden(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }

        private static string GetMimeType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".json" => "application/json",
                ".pdf" => "application/pdf",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream"
            };
        }

        private static void Reply(WebView2Control webView, string id, bool success, object? result, string? error)
        {
            if (string.IsNullOrEmpty(id))
                return;
            try
            {
                webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
                {
                    type = "widgetHostReply",
                    id,
                    ok = success,
                    result,
                    error = error ?? string.Empty
                }));
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
            {
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out JsonElement value))
            {
                return string.Empty;
            }
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static string GetRequiredString(JsonElement element, string name)
        {
            string value = GetString(element, name);
            return value.Length > 0
                ? value
                : throw new ArgumentException("缺少参数：" + name);
        }

        private static bool GetBoolean(JsonElement element, string name, bool defaultValue = false)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out JsonElement value))
            {
                return defaultValue;
            }
            if (value.ValueKind == JsonValueKind.True)
                return true;
            if (value.ValueKind == JsonValueKind.False)
                return false;
            string text = value.ToString();
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInteger(
            JsonElement element,
            string name,
            int defaultValue,
            int minimum,
            int maximum)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                int.TryParse(value.ToString(), out int parsed))
            {
                return Math.Clamp(parsed, minimum, maximum);
            }
            return Math.Clamp(defaultValue, minimum, maximum);
        }

        internal static bool IsWidgetInternalNavigation(string? uri)
        {
            return string.IsNullOrEmpty(uri) ||
                   uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) ||
                   uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
        }

        private static void OpenExternal(string? value, bool reportFailure = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (reportFailure)
                    throw new ArgumentException("要打开的目标不能为空", nameof(value));
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("HtmlWidgetCanvasWindow 打开外部目标失败：" + ex.Message);
                if (reportFailure)
                    throw new InvalidOperationException("系统无法打开指定目标：" + ex.Message, ex);
            }
        }

        private void SetWebViewsVisible(bool visible)
        {
            foreach (WidgetRuntime runtime in _runtimes.Values)
            {
                bool show = visible && runtime.ErrorPanel?.Visibility != Visibility.Visible;
                runtime.WebView.Visibility = show ? Visibility.Visible : Visibility.Hidden;
                runtime.WebView.IsHitTestVisible = show;
            }
        }

        private void UpdateEmptyState()
        {
            EmptyStatePanel.Visibility = _widgets.Any(widget =>
                widget.Home == HtmlWidgetHome.Canvas && !_detachedWindows.ContainsKey(widget))
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void MarkDirty()
        {
            if (_isClosed || _disposeRequested || Dispatcher.HasShutdownStarted)
                return;
            _saveDirty = true;
            _saveTimer.Stop();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(SaveDelayMilliseconds);
            _saveTimer.Start();
        }

        private bool SaveNow()
        {
            if (_isClosed)
                return true;
            _saveTimer.Stop();
            if (!_saveDirty && File.Exists(DataFilePath) && File.Exists(RuntimeDataFilePath))
                return true;
            try
            {
                HtmlWidgetCanvasStore.Save(DataFilePath, RuntimeDataFilePath, _widgets);
                _saveDirty = false;
                _saveTimer.Interval = TimeSpan.FromMilliseconds(SaveDelayMilliseconds);
                _lastSaveError = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("HtmlWidgetCanvasWindow 保存状态失败：" + ex.Message);
                if (!string.Equals(_lastSaveError, ex.Message, StringComparison.Ordinal))
                {
                    _lastSaveError = ex.Message;
                    if (IsVisible)
                        ShowToast("组件数据保存失败：" + ex.Message);
                }
                if (!_disposeRequested && !Dispatcher.HasShutdownStarted)
                {
                    _saveTimer.Interval = TimeSpan.FromMilliseconds(SaveRetryDelayMilliseconds);
                    _saveTimer.Start();
                }
                return false;
            }
        }

        private void ShowToast(string text)
        {
            if (_controlOverlayWindow?.IsVisible == true && _overlayToastPanel != null && _overlayToastText != null)
            {
                ToastPanel.Visibility = Visibility.Collapsed;
                _overlayToastText.Text = text;
                _overlayToastPanel.Visibility = Visibility.Visible;
            }
            else
            {
                if (_overlayToastPanel != null)
                    _overlayToastPanel.Visibility = Visibility.Collapsed;
                ToastText.Text = text;
                ToastPanel.Visibility = Visibility.Visible;
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private static string GetDisplayName(HtmlWidgetDefinition widget)
        {
            string title = GetHtmlTitle(widget.Html);
            return string.IsNullOrWhiteSpace(title) ? "未命名组件" : title;
        }

        private static string ExtractHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            string value = text.Trim();

            // 优先按完整 HTML 文档边界提取。组件代码本身可能包含 Markdown
            // 三反引号，不能把内容中的反引号误判为 AI 回复代码块的结束位置。
            int documentStart = value.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase);
            if (documentStart < 0)
                documentStart = value.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            int documentEnd = value.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
            if (documentStart >= 0 && documentEnd > documentStart)
                return value[documentStart..(documentEnd + "</html>".Length)].Trim();

            int marker = value.IndexOf("```html", StringComparison.OrdinalIgnoreCase);
            int markerLength = 7;
            if (marker < 0)
            {
                marker = value.IndexOf("```", StringComparison.Ordinal);
                markerLength = 3;
            }
            if (marker < 0)
                return value;
            int start = value.IndexOf('\n', marker);
            if (start < 0)
                start = marker + markerLength;
            else
                start++;
            int end = value.IndexOf("```", start, StringComparison.Ordinal);
            return end > start ? value[start..end].Trim() : value[start..].Trim();
        }

        private static string GetHtmlTitle(string html)
        {
            int titleStart = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
            if (titleStart < 0)
                return string.Empty;
            titleStart = html.IndexOf('>', titleStart);
            if (titleStart < 0)
                return string.Empty;
            int titleEnd = html.IndexOf("</title>", titleStart + 1, StringComparison.OrdinalIgnoreCase);
            if (titleEnd <= titleStart)
                return string.Empty;
            string title = WebUtility.HtmlDecode(html[(titleStart + 1)..titleEnd]).Trim();
            return title;
        }

        private static string SetHtmlTitle(string html, string title)
        {
            int titleStart = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
            if (titleStart < 0)
                return html;
            titleStart = html.IndexOf('>', titleStart);
            if (titleStart < 0)
                return html;
            int titleEnd = html.IndexOf("</title>", titleStart + 1, StringComparison.OrdinalIgnoreCase);
            if (titleEnd <= titleStart)
                return html;
            return html[..(titleStart + 1)] +
                   WebUtility.HtmlEncode(title) +
                   html[titleEnd..];
        }

        private string EnsureUniqueWidgetTitle(
            string html,
            HtmlWidgetDefinition? excludingWidget = null)
        {
            string title = GetHtmlTitle(html);
            if (string.IsNullOrWhiteSpace(title))
                return html;

            var usedTitles = _widgets
                .Where(widget => widget != excludingWidget)
                .Select(GetDisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string uniqueTitle = GetUniqueTitle(title, usedTitles);
            return string.Equals(title, uniqueTitle, StringComparison.Ordinal)
                ? html
                : SetHtmlTitle(html, uniqueTitle);
        }

        private bool NormalizeLoadedWidgetTitles()
        {
            var usedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (HtmlWidgetDefinition widget in _widgets)
            {
                string title = GetHtmlTitle(widget.Html);
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                string uniqueTitle = GetUniqueTitle(title, usedTitles);
                if (string.Equals(title, uniqueTitle, StringComparison.Ordinal))
                    continue;
                widget.Html = SetHtmlTitle(widget.Html, uniqueTitle);
                changed = true;
            }
            return changed;
        }

        private static string GetUniqueTitle(string title, HashSet<string> usedTitles)
        {
            string baseTitle = title.Trim();
            if (usedTitles.Add(baseTitle))
                return baseTitle;

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{baseTitle} {suffix}";
                if (usedTitles.Add(candidate))
                    return candidate;
            }
        }

        private sealed class WidgetRuntime(
            HtmlWidgetDefinition definition,
            Border frame,
            WebView2Control webView,
            Grid contentRoot)
        {
            public HtmlWidgetDefinition Definition { get; } = definition;
            public Border Frame { get; } = frame;
            public WebView2Control WebView { get; } = webView;
            public Grid ContentRoot { get; } = contentRoot;
            public Border? DragStrip { get; set; }
            public TextBlock? DragText { get; set; }
            public Border? ResizeHandle { get; set; }
            public Border? ErrorPanel { get; set; }
            public TextBlock? ErrorText { get; set; }
            public string? LastError { get; set; }
            public string? LastHostError { get; set; }
            public bool HasFatalError { get; set; }
        }

        private sealed class LibraryPreviewRuntime(
            HtmlWidgetDefinition definition,
            WebView2Control webView,
            int generation)
        {
            public HtmlWidgetDefinition Definition { get; } = definition;
            public WebView2Control WebView { get; } = webView;
            public int Generation { get; } = generation;
        }

        private sealed class ProcessOutputBudget(int maximumBytes)
        {
            private readonly object _syncRoot = new();
            private int _remainingBytes = maximumBytes;

            public bool Truncated { get; private set; }

            public int Reserve(int requestedBytes)
            {
                lock (_syncRoot)
                {
                    int reserved = Math.Min(requestedBytes, _remainingBytes);
                    _remainingBytes -= reserved;
                    if (reserved < requestedBytes)
                        Truncated = true;
                    return reserved;
                }
            }
        }

        internal const string WidgetHostBridgeScript = """
            (() => {
              if (window.widgetHost?.state?.read) return;
              const pending = new Map();
              let sequence = 0;
              chrome.webview.addEventListener('message', event => {
                const message = event.data;
                if (!message || message.type !== 'widgetHostReply') return;
                const request = pending.get(message.id);
                if (!request) return;
                pending.delete(message.id);
                if (message.ok) request.resolve(message.result);
                else request.reject(new Error(message.error || '宿主调用失败'));
              });
              const invoke = (method, args = {}) => new Promise((resolve, reject) => {
                const id = `${Date.now()}-${++sequence}`;
                pending.set(id, { resolve, reject });
                try {
                  chrome.webview.postMessage({ id, method, args });
                } catch (error) {
                  pending.delete(id);
                  reject(error);
                }
              });
              window.widgetHost = {
                state: {
                  read: (key, defaultValue = null) => invoke('state.read', { key, defaultValue }),
                  write: (key, value) => invoke('state.write', { key, value }),
                  remove: key => invoke('state.remove', { key }),
                  clear: () => invoke('state.clear'),
                  flush: () => invoke('state.flush')
                },
                clipboard: {
                  read: () => invoke('clipboard.read'),
                  write: text => invoke('clipboard.write', { text })
                },
                url: {
                  open: (args = {}) => invoke('url.open', args)
                },
                path: {
                  open: (args = {}) => invoke('path.open', args)
                },
                window: {
                  hide: () => invoke('window.hide')
                },
                http: {
                  get: (args = {}) => invoke('http.get', args),
                  post: (args = {}) => invoke('http.post', args),
                  request: (args = {}) => invoke('http.request', args)
                },
                fs: {
                  getKnownFolders: () => invoke('fs.getKnownFolders'),
                  exists: (args = {}) => invoke('fs.exists', args),
                  getInfo: (args = {}) => invoke('fs.getInfo', args),
                  readText: (args = {}) => invoke('fs.readText', args),
                  readBase64: (args = {}) => invoke('fs.readBase64', args),
                  list: (args = {}) => invoke('fs.list', args),
                  selectFile: (args = {}) => invoke('fs.selectFile', args),
                  selectFolder: (args = {}) => invoke('fs.selectFolder', args)
                },
                process: {
                  start: (args = {}) => invoke('process.start', args),
                  run: (args = {}) => invoke('process.run', args)
                }
              };
              const reportRuntimeError = (message, stack = '') =>
                chrome.webview.postMessage({
                  type: 'runtimeError',
                  message: String(message || '未知脚本错误'),
                  stack: String(stack || '')
                });
              window.addEventListener('error', event =>
                reportRuntimeError(event.message, event.error?.stack || ''), true);
              window.addEventListener('unhandledrejection', event => {
                const reason = event.reason;
                reportRuntimeError(reason?.message || reason || '未处理的异步错误', reason?.stack || '');
              }, true);
              document.addEventListener('compositionstart', () =>
                chrome.webview.postMessage({ type: 'compositionState', active: true }), true);
              document.addEventListener('compositionend', () =>
                chrome.webview.postMessage({ type: 'compositionState', active: false }), true);
            })();
            """;

        [DllImport("user32.dll")]
        private static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);

        private const int ExtendedWindowStyleIndex = -20;
        private const int ExtendedStyleNoActivate = 0x08000000;
        private const int ExtendedStyleToolWindow = 0x00000080;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(nint windowHandle, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid,
            uint flags,
            IntPtr token,
            out IntPtr path);
    }
}
