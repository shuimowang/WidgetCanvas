#nullable enable

using WidgetCanvas.HtmlWidgets;
using WidgetCanvas.Infrastructure.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetCanvas.Windows
{
    /// <summary>
    /// 独立组件窗口关闭时默认返回的位置。
    /// </summary>
    public enum HtmlWidgetReturnTarget
    {
        Canvas,
        Library
    }

    /// <summary>
    /// 承载一个浮岛 HTML 组件的独立桌面窗口。
    /// </summary>
    public sealed partial class HtmlWidgetWindow : Window
    {
        private const double WindowChromeHeight = 37;
        private readonly HtmlWidgetCanvasWindow _host;
        private readonly HtmlWidgetDefinition _definition;
        private readonly string _initialPosition;
        private bool _loaded;
        private bool _allowClose;
        private bool _closePending;
        private bool _hidePending;
        private bool _browserInitializing;
        private bool _browserEventsConfigured;
        private bool _bridgeInstalled;
        private bool _browserStartDeferred;
        private bool _browserReadyForDock;
        private bool _closed;
        private int _transitionVersion;
        private int _dockRegistrationVersion;
        private DetachedCloseAction _closeAction;
        private string? _lastRuntimeError;
        private string? _lastHostError;

        internal HtmlWidgetWindow(
            HtmlWidgetCanvasWindow host,
            HtmlWidgetDefinition definition,
            string? initialPosition,
            bool deferBrowserStart,
            HtmlWidgetReturnTarget returnTarget)
        {
            _host = host;
            _definition = definition;
            _initialPosition = string.IsNullOrWhiteSpace(initialPosition)
                ? definition.DetachedPosition
                : initialPosition.Trim();
            _browserStartDeferred = deferBrowserStart;
            ReturnTarget = returnTarget;
            _closeAction = GetOriginCloseAction();

            InitializeComponent();
            Title = ComponentName;
            TitleText.Text = ComponentName;
            Width = Math.Max(
                MinWidth,
                definition.DetachedWidth > 0 ? definition.DetachedWidth : definition.Width);
            Height = Math.Max(
                MinHeight,
                (definition.DetachedHeight > 0 ? definition.DetachedHeight : definition.Height) +
                WindowChromeHeight);
            Topmost = definition.DetachedTopmost;
            OriginButton.ToolTip = OriginToolTip;
            Browser.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            Browser.AllowExternalDrop = false;

            SourceInitialized += Window_SourceInitialized;
            Loaded += Window_Loaded;
            Closing += Window_Closing;
            Closed += Window_Closed;
            LocationChanged += (_, _) => SaveWindowLayout();
            SizeChanged += (_, _) => SaveWindowLayout();
            WidgetContentHost.SizeChanged += (_, _) => SaveWindowLayout();
        }

        /// <summary>
        /// 获取来自组件 HTML <c>title</c> 的显示名称。
        /// </summary>
        public string ComponentName => _host.GetWidgetDisplayName(_definition);

        /// <summary>
        /// 获取标题栏 × 与系统关闭操作默认返回的位置。
        /// </summary>
        public HtmlWidgetReturnTarget ReturnTarget { get; }

        /// <summary>
        /// 获取或设置此组件窗口是否在上、左、右屏幕边缘自动隐藏。
        /// 默认关闭；只使用普通位置调整，不改变窗口父子关系或 Z-order。
        /// </summary>
        public bool IsEdgeAutoHideEnabled
        {
            get => _definition.DetachedAutoHide;
            set
            {
                Dispatcher.VerifyAccess();
                if (_definition.DetachedAutoHide == value)
                    return;
                _definition.DetachedAutoHide = value;
                _host.MarkWidgetDataDirty();
                if (value)
                    ScheduleDockRegistration();
                else
                    SuspendDockRegistration();
            }
        }

        internal HtmlWidgetDefinition Definition => _definition;

        internal bool HasActiveComposition => _host.IsWebViewComposing(Browser);

        internal void ShowAndActivate(bool activate)
        {
            if (_closed || _allowClose || _closePending)
                throw new InvalidOperationException("组件窗口正在关闭，不能再次显示。");

            // 显示请求可以撤销尚在等待输入法结束的隐藏，但不能撤销已经确认的
            // 删除、编辑、收回或关闭请求。
            if (_hidePending)
                _transitionVersion++;
            SuspendDockRegistration();
            ShowActivated = activate;
            if (!IsVisible)
                base.Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            if (activate)
                Activate();
            ScheduleDockRegistration();
        }

        /// <summary>
        /// 显示窗口，并按需激活。适合在 <see cref="HideWidget"/> 后复用当前实例。
        /// </summary>
        public void ShowWidget(bool activate = true) => ShowAndActivate(activate);

        /// <summary>
        /// 显示窗口并激活。覆盖同名 WPF 入口，使刚发出的异步隐藏请求可以被安全撤销。
        /// </summary>
        public new void Show() => ShowWidget();

        /// <summary>
        /// 隐藏窗口但保留 WebView2 和组件运行状态。该方法覆盖同名的 WPF
        /// 入口，确保外部常规调用也经过输入法与布局保护。
        /// </summary>
        public new void Hide() => HideWidget();

        /// <summary>
        /// 使用当前已保存的 HTML 重新加载组件。
        /// </summary>
        public void Reload()
        {
            Title = ComponentName;
            TitleText.Text = ComponentName;
            ClearError();
            LoadingPanel.Visibility = Visibility.Visible;
            Browser.Visibility = Visibility.Hidden;
            ErrorPanel.Visibility = Visibility.Collapsed;
            if (_browserStartDeferred)
                return;
            if (Browser.CoreWebView2 == null)
            {
                _ = InitializeBrowserAsync();
                return;
            }
            try
            {
                Browser.CoreWebView2.NavigateToString(_definition.Html);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                ShowFatalError("重新加载失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 隐藏窗口但保留 WebView2 和组件运行状态。
        /// </summary>
        public void HideWidget() => RequestHide();

        /// <summary>
        /// 关闭独立窗口并返回创建它的浮岛或组件库入口。
        /// </summary>
        public void ReturnToOrigin() => RequestClose(GetOriginCloseAction());

        /// <summary>
        /// 关闭独立窗口，将组件放回主浮岛画布并显示主浮岛。
        /// </summary>
        public void ReturnToCanvas() => RequestClose(DetachedCloseAction.Canvas);

        /// <summary>
        /// 关闭独立窗口并把组件保留在组件库中。
        /// </summary>
        public void MoveToLibrary() => RequestClose(DetachedCloseAction.Library);

        internal void EditWidget() => RequestClose(DetachedCloseAction.Edit);

        internal void DeleteAfterConfirmation() => RequestClose(DetachedCloseAction.Delete);

        internal void CloseForHost()
        {
            if (_closed || _allowClose)
                return;

            // 宿主已统一处理输入法等待。这里必须同步终止，不能让异步关闭在
            // 旧宿主销毁后继续保存并覆盖新宿主的数据。
            _transitionVersion++;
            _closeAction = DetachedCloseAction.Host;
            SaveWindowLayout();
            _allowClose = true;
            Close();
        }

        internal void SaveLayoutForHost() => SaveWindowLayout();

        internal void StartBrowserAfterTransfer()
        {
            if (_closed || _allowClose || !_browserStartDeferred)
                return;
            _browserStartDeferred = false;
            if (_loaded)
                _ = InitializeBrowserAsync();
        }

        internal void ApplyOwner(Window? owner)
        {
            Owner = owner is { IsLoaded: true } && owner != this
                ? owner
                : null;
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            string position = NormalizeDetachedPosition(_initialPosition);
            WindowHelper.SetLocation(
                this,
                string.IsNullOrWhiteSpace(position)
                    ? WindowLocation.NearMouse
                    : WindowLocation.Auto,
                position);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded)
                return;
            _loaded = true;
            Opacity = 1;
            if (!_browserStartDeferred)
                await InitializeBrowserAsync();
            SaveWindowLayout();
        }

        private async Task InitializeBrowserAsync()
        {
            if (_browserInitializing)
                return;
            _browserInitializing = true;
            string stage = "创建运行环境";
            try
            {
                CoreWebView2Environment environment = await HtmlWidgetCanvasWindow.GetWidgetEnvironmentAsync();
                stage = "创建浏览器控制器";
                await Browser.EnsureCoreWebView2Async(environment);
                if (Browser.CoreWebView2 == null)
                    return;

                CoreWebView2 core = Browser.CoreWebView2;
                if (!_browserEventsConfigured)
                {
                    stage = "配置浏览器";
                    core.Settings.IsStatusBarEnabled = false;
                    core.ContextMenuRequested += (_, args) =>
                    {
                        args.Handled = true;
                        _ = Dispatcher.BeginInvoke(OpenContextMenu);
                    };
                    core.NewWindowRequested += (_, args) =>
                    {
                        args.Handled = true;
                        Debug.WriteLine("HtmlWidgetWindow 已拦截组件新窗口：" + args.Uri);
                    };
                    core.NavigationStarting += (_, args) =>
                    {
                        if (HtmlWidgetCanvasWindow.IsWidgetInternalNavigation(args.Uri))
                            return;
                        args.Cancel = true;
                        Debug.WriteLine("HtmlWidgetWindow 已拦截组件页面导航：" + args.Uri);
                    };
                    core.WebMessageReceived += (_, args) =>
                        _host.HandleDetachedWebMessage(
                            _definition,
                            Browser,
                            args,
                            MarkRuntimeError,
                            MarkHostError,
                            HideWidget,
                            this);
                    core.NavigationCompleted += (_, args) =>
                    {
                        if (args.IsSuccess)
                        {
                            _browserReadyForDock = true;
                            LoadingPanel.Visibility = Visibility.Collapsed;
                            ErrorPanel.Visibility = Visibility.Collapsed;
                            Browser.Visibility = Visibility.Visible;
                            ScheduleDockRegistration();
                        }
                        else
                        {
                            ShowFatalError("页面加载失败：" + args.WebErrorStatus);
                        }
                    };
                    core.ProcessFailed += (_, args) =>
                        ShowFatalError("浏览器进程异常：" + args.ProcessFailedKind);
                    _browserEventsConfigured = true;
                }

                if (!_bridgeInstalled)
                {
                    stage = "注入宿主脚本";
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(
                        HtmlWidgetCanvasWindow.WidgetHostBridgeScript);
                    _bridgeInstalled = true;
                }
                stage = "加载组件 HTML";
                core.NavigateToString(_definition.Html);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                string message = $"WebView2 {stage}失败：{ex.Message}";
                Debug.WriteLine(message + Environment.NewLine + ex);
                ShowFatalError(message);
            }
            finally
            {
                _browserInitializing = false;
            }
        }

        private void MarkRuntimeError(string message)
        {
            _lastRuntimeError = string.IsNullOrWhiteSpace(message) ? "未知组件错误" : message.Trim();
            StatusDot.Foreground = new SolidColorBrush(Color.FromRgb(230, 112, 129));
            StatusDot.ToolTip = "组件发生错误 · 右键查看";
        }

        private void MarkHostError(string message)
        {
            _lastHostError = string.IsNullOrWhiteSpace(message)
                ? "未知宿主调用错误"
                : message.Trim();
        }

        private void ShowFatalError(string message)
        {
            MarkRuntimeError(message);
            Browser.Visibility = Visibility.Hidden;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = HtmlWidgetCanvasWindow.GetShortWidgetError(message);
            ErrorText.ToolTip = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private void ClearError()
        {
            _lastRuntimeError = null;
            StatusDot.Foreground = new SolidColorBrush(Color.FromRgb(109, 211, 158));
            StatusDot.ToolTip = "组件运行中";
        }

        private void SaveWindowLayout()
        {
            if (!_loaded || WindowState != WindowState.Normal || _allowClose && !IsVisible)
                return;

            if (WidgetContentHost.ActualWidth >= 1 && WidgetContentHost.ActualHeight >= 1)
            {
                _definition.DetachedWidth = Math.Max(140, WidgetContentHost.ActualWidth);
                _definition.DetachedHeight = Math.Max(90, WidgetContentHost.ActualHeight);
            }
            if (_host.CanSaveDetachedPosition(this))
                _definition.DetachedPosition = HtmlWidgetCanvasWindow.GetPhysicalWindowPosition(this);
            _definition.DetachedTopmost = Topmost;
            _host.MarkWidgetDataDirty();
        }

        private static string NormalizeDetachedPosition(string position)
        {
            if (string.IsNullOrWhiteSpace(position))
                return string.Empty;
            string[] parts = position.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 4 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int left) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int top) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
                width <= 0 || height <= 0)
            {
                return string.Empty;
            }

            int centerX = left + width / 2;
            int centerY = top + height / 2;
            if (!WindowHelper.TryGetMonitorBoundsFromPoint(centerX, centerY, out _, out RECT workArea))
                return string.Empty;
            width = Math.Min(width, Math.Max(1, workArea.Width));
            height = Math.Min(height, Math.Max(1, workArea.Height));
            left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            return $"{left},{top},{width},{height}";
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
            SaveWindowLayout();
        }

        private void Header_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            OpenContextMenu();
            e.Handled = true;
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
            SaveWindowLayout();
        }

        private void OpenContextMenu()
        {
            var menu = new ContextMenu();
            HtmlWidgetMenuTheme.Apply(menu);
            menu.Items.Add(CreateMenuItem("编辑组件…", () => RequestClose(DetachedCloseAction.Edit)));
            menu.Items.Add(CreateMenuItem("复制 AI 修改提示词", CopyEditPrompt));
            menu.Items.Add(CreateMenuItem("重新加载", Reload));
            var diagnostics = new MenuItem { Header = "错误诊断" };
            if (!string.IsNullOrWhiteSpace(_lastRuntimeError))
            {
                diagnostics.Items.Add(CreateMenuItem("复制组件错误", () =>
                {
                    try
                    {
                        Clipboard.SetText(_lastRuntimeError);
                    }
                    catch (Exception ex)
                    {
                        MarkRuntimeError("复制失败：" + ex.Message);
                    }
                }));
            }
            if (!string.IsNullOrWhiteSpace(_lastHostError))
            {
                diagnostics.Items.Add(CreateMenuItem("复制最近宿主调用错误", () =>
                {
                    try
                    {
                        Clipboard.SetText(_lastHostError);
                    }
                    catch (Exception ex)
                    {
                        MarkHostError("复制失败：" + ex.Message);
                    }
                }));
            }
            if (diagnostics.Items.Count > 0)
                menu.Items.Add(diagnostics);
            menu.Items.Add(new Separator());
            var topmostItem = new MenuItem
            {
                Header = "窗口置顶",
                IsCheckable = true,
                IsChecked = Topmost
            };
            topmostItem.Click += (_, _) =>
            {
                Topmost = topmostItem.IsChecked;
                _definition.DetachedTopmost = Topmost;
                _host.MarkWidgetDataDirty();
            };
            menu.Items.Add(topmostItem);
            var autoHideItem = new MenuItem
            {
                Header = "贴边自动隐藏",
                ToolTip = "拖到屏幕上、左或右边缘后自动隐藏",
                IsCheckable = true,
                IsChecked = IsEdgeAutoHideEnabled
            };
            autoHideItem.Click += (_, _) => IsEdgeAutoHideEnabled = autoHideItem.IsChecked;
            menu.Items.Add(autoHideItem);
            menu.Items.Add(CreateMenuItem("隐藏窗口", HideWidget));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(OriginMenuText, ReturnToOrigin));
            menu.Items.Add(CreateSubmenu(
                "移动到",
                CreateMenuItem("浮岛", ReturnToCanvas),
                CreateMenuItem("组件库", MoveToLibrary)));
            menu.Items.Add(new Separator());
            MenuItem deleteItem = CreateMenuItem(
                "删除组件…",
                () => _host.DeleteDetachedWidget(_definition, this));
            deleteItem.Foreground = new SolidColorBrush(Color.FromRgb(255, 157, 165));
            menu.Items.Add(deleteItem);
            menu.IsOpen = true;
        }

        private static MenuItem CreateMenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        private void CopyEditPrompt()
        {
            try
            {
                Clipboard.SetText(HtmlWidgetCanvasPrompt.BuildEdit(_definition));
                HideWidget();
            }
            catch (Exception ex)
            {
                MarkHostError("复制失败：" + ex.Message);
            }
        }

        private static MenuItem CreateSubmenu(string header, params object[] items)
        {
            var menu = new MenuItem { Header = header };
            foreach (object item in items)
                menu.Items.Add(item);
            return menu;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => ReturnToOrigin();

        private void EditButton_Click(object sender, RoutedEventArgs e) => EditWidget();

        private void RetryButton_Click(object sender, RoutedEventArgs e) => Reload();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;
            HideWidget();
            e.Handled = true;
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_allowClose || Dispatcher.HasShutdownStarted)
                return;
            e.Cancel = true;
            // Closing 事件尚未退出时再次 Close 会触发 WPF 关闭重入异常。
            _ = Dispatcher.BeginInvoke(ReturnToOrigin);
        }

        private async void RequestHide()
        {
            if (_closed || _allowClose || _hidePending || _closePending || !IsVisible)
                return;
            int version = ++_transitionVersion;
            _hidePending = true;
            try
            {
                await FinishCompositionAsync();
                if (_closed || _allowClose || _closePending || version != _transitionVersion)
                    return;
                SuspendDockRegistration();
                SaveWindowLayout();
                base.Hide();
            }
            finally
            {
                _hidePending = false;
            }
        }

        private async void RequestClose(DetachedCloseAction action)
        {
            if (_closed || _allowClose || _closePending)
                return;
            int version = ++_transitionVersion;
            _closePending = true;
            _closeAction = action;
            try
            {
                await FinishCompositionAsync();
                if (_closed || _allowClose || version != _transitionVersion)
                    return;
                SuspendDockRegistration();
                SaveWindowLayout();
                _allowClose = true;
                Close();
            }
            finally
            {
                _closePending = false;
            }
        }

        private async Task FinishCompositionAsync()
        {
            if (!_host.IsWebViewComposing(Browser))
                return;
            if (IsActive)
            {
                HtmlWidgetCanvasWindow.CancelWidgetComposition();
                await Task.Delay(300);
            }
            _host.ClearWebViewComposition(Browser);
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _closed = true;
            _transitionVersion++;
            SuspendDockRegistration();
            _host.ClearWebViewComposition(Browser);
            Browser.Dispose();
            _host.DetachedWidgetClosed(_definition, this, _closeAction);
        }

        internal void HandleCompositionStateChanged(bool active)
        {
            if (active)
                SuspendDockRegistration();
            else
                ScheduleDockRegistration();
        }

        private async void ScheduleDockRegistration()
        {
            int version = ++_dockRegistrationVersion;
            if (!_definition.DetachedAutoHide || !_browserReadyForDock ||
                _closed || _allowClose || !IsVisible)
            {
                return;
            }

            await Task.Delay(700);
            if (version != _dockRegistrationVersion || !_definition.DetachedAutoHide ||
                !_browserReadyForDock || _closed || _allowClose || !IsVisible ||
                _host.IsWebViewComposing(Browser))
            {
                return;
            }
            _host.RegisterDetachedDocking(this);
        }

        private void SuspendDockRegistration()
        {
            _dockRegistrationVersion++;
            _host.UnregisterDetachedDocking(this);
        }

        private DetachedCloseAction GetOriginCloseAction() =>
            ReturnTarget == HtmlWidgetReturnTarget.Canvas
                ? DetachedCloseAction.Canvas
                : DetachedCloseAction.Library;

        private string OriginMenuText => ReturnTarget == HtmlWidgetReturnTarget.Canvas
            ? "放回浮岛"
            : "放回组件库";

        private string OriginToolTip => ReturnTarget == HtmlWidgetReturnTarget.Canvas
            ? "关闭并放回浮岛"
            : "关闭并放回组件库";

        internal enum DetachedCloseAction
        {
            Library,
            Canvas,
            Edit,
            Delete,
            Host
        }
    }
}
