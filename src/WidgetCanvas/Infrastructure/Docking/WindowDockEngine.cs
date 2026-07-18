using WidgetCanvas.Infrastructure.Hooks;
using WidgetCanvas.Infrastructure.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WidgetCanvas.Infrastructure.Docking
{
    /// <summary>
    /// 提供默认单例形式的窗口贴边管理入口。
    /// <para>
    /// 该类面向大多数直接使用场景，内部按需持有一个默认的 <see cref="WindowDockEngine"/> 实例。
    /// </para>
    /// <para>
    /// 常见用法：
    /// <list type="number">
    /// <item><description>调用 <see cref="Configure"/> 配置选项（可选）。</description></item>
    /// <item><description>调用 <see cref="TryAdd"/> 或 <see cref="Toggle"/> 管理窗口。</description></item>
    /// <item><description>通常无需显式调用 <see cref="Start"/>；在首次成功加入窗口时会自动启动引擎。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class WindowDockManager
    {
        private static readonly object Gate = new object();
        private static WindowDockEngine? _engine;
        private static WindowDockOptions _defaultOptions = new WindowDockOptions();

        /// <summary>
        /// 获取一个值，表示默认引擎当前是否正在运行。
        /// </summary>
        public static bool IsRunning
        {
            get
            {
                lock (Gate)
                {
                    CompactEngine_NoLock();
                    return _engine != null && _engine.IsRunning;
                }
            }
        }

        /// <summary>
        /// 获取当前默认引擎受管窗口数量。
        /// <para>
        /// 若默认引擎尚未创建，则返回 0。
        /// </para>
        /// </summary>
        public static int Count
        {
            get
            {
                lock (Gate)
                {
                    CompactEngine_NoLock();
                    return _engine == null ? 0 : _engine.Count;
                }
            }
        }

        /// <summary>
        /// 配置默认引擎。
        /// <para>
        /// 若默认引擎尚未创建，则仅记录默认配置；
        /// 若默认引擎已存在，则会立即更新其配置。
        /// </para>
        /// <para>
        /// 传入 <see langword="null"/> 时会重置为默认配置。
        /// </para>
        /// <para>
        /// 默认引擎后续若被停止并释放，再次使用静态入口时会按最近一次配置重新创建。
        /// </para>
        /// </summary>
        /// <param name="options">新的配置对象。传入 <see langword="null"/> 时使用默认配置。</param>
        public static void Configure(WindowDockOptions? options = null)
        {
            lock (Gate)
            {
                _defaultOptions = WindowDockOptions.Clone(options);
                CompactEngine_NoLock();

                if (_engine != null)
                {
                    _engine.UpdateOptions(_defaultOptions);
                }
            }
        }

        /// <summary>
        /// 启动默认引擎。
        /// <para>
        /// 该方法是幂等的；若已处于运行状态，不会重复安装 Hook 或重复创建计时器。
        /// </para>
        /// </summary>
        public static void Start()
        {
            WindowDockEngine engine = EnsureEngine();
            engine.Start();
        }

        /// <summary>
        /// 停止默认引擎，恢复当前所有受管窗口的显示状态，并清空管理列表。
        /// <para>
        /// 恢复过程中会尝试将窗口重新带回用户可见层级或置于前方，
        /// 但实际前台与焦点行为仍受系统窗口管理规则限制。
        /// </para>
        /// <para>
        /// 停止后会释放当前默认引擎实例；后续再次使用时会按需重新创建。
        /// </para>
        /// </summary>
        public static void Stop()
        {
            WindowDockEngine? engine;

            lock (Gate)
            {
                engine = _engine;
                _engine = null;
            }

            if (engine != null)
            {
                try
                {
                    engine.Dispose();
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                }
            }
        }

        /// <summary>
        /// 检查指定窗口当前是否适合加入贴边管理。
        /// <para>
        /// 默认仅执行最小基础检查：
        /// 句柄是否为空、是否为有效窗口、是否已受管理。
        /// </para>
        /// <para>
        /// 若设置了 <see cref="WindowDockOptions.CustomWindowFilter"/>，
        /// 还会执行调用方自定义过滤规则。
        /// </para>
        /// <para>
        /// 该方法为查询操作；若默认引擎尚未创建，会基于当前默认配置临时创建一个引擎实例用于检查。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要检查的窗口句柄。</param>
        /// <returns>检查结果。</returns>
        public static DockCheckResult Check(IntPtr hwnd)
        {
            WindowDockEngine engine = EnsureEngine();
            return engine.Check(hwnd);
        }

        /// <summary>
        /// 判断指定窗口当前是否适合加入贴边管理。
        /// <para>
        /// 该方法为查询操作；若默认引擎尚未创建，会基于当前默认配置临时创建一个引擎实例用于检查。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要检查的窗口句柄。</param>
        /// <returns>若适合加入管理，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool CanManage(IntPtr hwnd)
        {
            WindowDockEngine engine = EnsureEngine();
            return engine.CanManage(hwnd);
        }

        /// <summary>
        /// 尝试将指定窗口加入贴边管理。
        /// <para>
        /// 成功时若引擎尚未启动，会自动启动引擎。
        /// </para>
        /// <para>
        /// 默认仅执行最小基础检查；
        /// 若调用方需要更严格的窗口筛选，可通过
        /// <see cref="WindowDockOptions.CustomWindowFilter"/> 自行补充。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要加入管理的窗口句柄。</param>
        /// <returns>加入成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryAdd(IntPtr hwnd)
        {
            WindowDockEngine engine = EnsureEngine();
            return engine.TryAdd(hwnd);
        }

        /// <summary>
        /// 尝试将指定窗口加入贴边管理，并返回检查结果。
        /// <para>
        /// 成功时若引擎尚未启动，会自动启动引擎。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要加入管理的窗口句柄。</param>
        /// <param name="result">检查结果或加入结果说明。</param>
        /// <returns>加入成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryAdd(IntPtr hwnd, out DockCheckResult result)
        {
            WindowDockEngine engine = EnsureEngine();
            return engine.TryAdd(hwnd, out result);
        }

        /// <summary>
        /// 将指定窗口从贴边管理中移除，并恢复其显示状态。
        /// <para>
        /// 为提升可见性，恢复后会尝试将该窗口带回用户可见层级或置于前方，
        /// 但实际前台与焦点行为仍受系统窗口管理规则限制。
        /// </para>
        /// <para>
        /// 若移除后已无受管窗口，引擎会自动停止；默认引擎实例也会随之释放。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要移除的窗口句柄。</param>
        /// <returns>若该窗口原本受管且已成功移除，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool Remove(IntPtr hwnd)
        {
            WindowDockEngine? engine;

            lock (Gate)
            {
                CompactEngine_NoLock();
                if (_engine == null)
                    return false;

                engine = _engine;
            }

            bool result = engine.Remove(hwnd);
            CompactEngine(engine);
            return result;
        }

        /// <summary>
        /// 切换指定窗口的贴边管理状态。
        /// <para>
        /// 若窗口当前未受管，则尝试加入管理并在成功时返回 <see langword="true"/>；
        /// 若窗口当前已受管，则将其移除并恢复显示状态，返回 <see langword="false"/>。
        /// </para>
        /// <para>
        /// 若窗口当前未受管但检查未通过，则不会加入管理，并返回 <see langword="false"/>。
        /// </para>
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// <see langword="true"/> 表示调用后该窗口处于受管状态；
        /// <see langword="false"/> 表示调用后该窗口不处于受管状态。
        /// </returns>
        public static bool Toggle(IntPtr hwnd)
        {
            // 已受管时直接走 Remove 路径，避免为移除操作多余地创建新引擎实例
            if (Contains(hwnd))
            {
                Remove(hwnd);
                return false;
            }

            WindowDockEngine engine = EnsureEngine();
            bool result = engine.Toggle(hwnd);
            CompactEngine(engine);
            return result;
        }

        /// <summary>
        /// 判断指定窗口当前是否已受默认引擎管理。
        /// <para>
        /// 该方法不会为单纯查询而创建新的默认引擎实例。
        /// </para>
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>若已受管，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool Contains(IntPtr hwnd)
        {
            lock (Gate)
            {
                CompactEngine_NoLock();
                return _engine != null && _engine.Contains(hwnd);
            }
        }

        /// <summary>
        /// 清空当前所有受管窗口，并全部恢复。
        /// <para>
        /// 该方法等价于停止引擎并释放默认引擎实例。
        /// </para>
        /// </summary>
        public static void Clear()
        {
            Stop();
        }

        /// <summary>
        /// 恢复当前所有受管窗口的显示状态，但不移除管理关系。
        /// <para>
        /// 恢复过程中会尝试提升窗口可见性；
        /// 实际前台与焦点行为仍受系统窗口管理规则限制。
        /// </para>
        /// <para>
        /// 该方法不会为单纯恢复操作而创建新的默认引擎实例。
        /// </para>
        /// </summary>
        public static void RestoreAll()
        {
            WindowDockEngine? engine;

            lock (Gate)
            {
                CompactEngine_NoLock();

                engine = _engine;
                if (engine == null)
                    return;
            }

            engine.RestoreAll();
            CompactEngine(engine);
        }

        /// <summary>
        /// 获取当前受管窗口状态快照数组。
        /// <para>
        /// 返回的是只读快照；数组及其中元素都不再与引擎内部状态对象共享可变引用。
        /// </para>
        /// <para>
        /// 若默认引擎尚未创建，则返回空数组。
        /// </para>
        /// </summary>
        /// <returns>当前受管窗口状态快照数组。</returns>
        public static WindowDockSnapshot[] GetItems()
        {
            lock (Gate)
            {
                CompactEngine_NoLock();
                return _engine == null ? Array.Empty<WindowDockSnapshot>() : _engine.GetItems();
            }
        }

        /// <summary>
        /// 设置运行期间的状态快照回调。
        /// <para>
        /// 回调主要用于调试、监视或轻量状态展示，不保证覆盖每一次内部状态变化。
        /// </para>
        /// <para>
        /// 回调在线程池线程执行；若调用方需要更新 UI，请自行切回 UI 线程。
        /// </para>
        /// <para>
        /// 传入 <see langword="null"/> 可取消回调。若默认引擎尚未创建且传入为 <see langword="null"/>，则不会创建引擎。
        /// </para>
        /// </summary>
        /// <param name="callback">快照回调。传入 <see langword="null"/> 可取消回调。</param>
        public static void SetSnapshotCallback(Action<WindowDockSnapshot[]>? callback)
        {
            if (callback == null)
            {
                lock (Gate)
                {
                    CompactEngine_NoLock();
                    if (_engine != null)
                    {
                        _engine.SetSnapshotCallback(null);
                    }
                    CompactEngine_NoLock();
                }

                return;
            }

            WindowDockEngine engine = EnsureEngine();
            engine.SetSnapshotCallback(callback);
        }

        /// <summary>
        /// 立即触发一次内部检查。
        /// <para>
        /// 主要用于调试或手动驱动检查逻辑。
        /// </para>
        /// <para>
        /// 该方法不会为单纯检查操作而创建新的默认引擎实例。
        /// </para>
        /// </summary>
        public static void Tick()
        {
            WindowDockEngine? engine;

            lock (Gate)
            {
                CompactEngine_NoLock();

                engine = _engine;
                if (engine == null)
                    return;
            }

            engine.Tick();
            CompactEngine(engine);
        }

        private static WindowDockEngine EnsureEngine()
        {
            lock (Gate)
            {
                CompactEngine_NoLock();

                if (_engine == null)
                {
                    _engine = new WindowDockEngine(_defaultOptions);
                }

                return _engine;
            }
        }

        private static void CompactEngine(WindowDockEngine engine)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_engine, engine))
                    return;

                CompactEngine_NoLock();
            }
        }

        private static void CompactEngine_NoLock()
        {
            if (_engine == null)
                return;

            if (_engine.IsDisposed)
            {
                _engine = null;
                return;
            }

            if (!_engine.IsRunning && _engine.Count == 0)
            {
                try
                {
                    _engine.Dispose();
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                }
                finally
                {
                    _engine = null;
                }
            }
        }

        [Conditional("DEBUG")]
        private static void DebugIgnore(Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// 表示窗口贴边管理的配置项。
    /// <para>
    /// 大多数情况下使用默认值即可。
    /// </para>
    /// <para>
    /// 文档中的"建议范围"仅供参考；
    /// 内部规范化逻辑仅修正无效值，不会强制裁剪到建议区间。
    /// </para>
    /// </summary>
    public sealed class WindowDockOptions
    {
        /// <summary>
        /// 获取或设置窗口移动动画时长（秒）。
        /// <para>
        /// 控制窗口贴边隐藏、从边缘弹出、回弹修正时的持续时间。
        /// </para>
        /// <para>
        /// 建议范围：0.15 ~ 0.40。
        /// 设为 0 可关闭动画，改为瞬移。
        /// </para>
        /// </summary>
        public double AnimationDurationSeconds { get; set; } = 0.30;

        /// <summary>
        /// 获取或设置鼠标移动检测节流间隔（毫秒）。
        /// <para>
        /// 数值越小，响应越灵敏；数值越大，CPU 占用更低。
        /// </para>
        /// <para>
        /// 建议范围：30 ~ 60。
        /// 设为 0 或负值时，内部会自动重置为默认值 40；节流不支持关闭。
        /// </para>
        /// </summary>
        public int ThrottleMilliseconds { get; set; } = 40;

        /// <summary>
        /// 获取或设置边缘触发检测距离（像素）。
        /// <para>
        /// 当鼠标进入屏幕边缘该距离以内时，会触发已贴边隐藏窗口的唤出逻辑。
        /// </para>
        /// <para>
        /// 建议范围：8 ~ 16。
        /// </para>
        /// </summary>
        public int EdgeTriggerPixels { get; set; } = 10;

        /// <summary>
        /// 获取或设置鼠标离开窗口后触发隐藏所允许的边距（像素）。
        /// <para>
        /// 用于降低窗口在边缘附近反复隐藏、显示的抖动。
        /// </para>
        /// <para>
        /// 建议范围：12 ~ 20。
        /// </para>
        /// </summary>
        public int MouseLeaveMarginPixels { get; set; } = 14;

        /// <summary>
        /// 获取或设置贴边状态释放的滞回距离（像素）。
        /// <para>
        /// 用于避免窗口在边缘附近来回抖动时，频繁在贴边与非贴边状态之间切换。
        /// </para>
        /// <para>
        /// 建议范围：10 ~ 20。
        /// </para>
        /// </summary>
        public int SideReleaseHysteresisPixels { get; set; } = 14;

        /// <summary>
        /// 获取或设置窗口贴边隐藏时，是否从任务栏与 Alt+Tab 中移除。
        /// <para>
        /// 启用后会临时修改窗口扩展样式：
        /// <c>WS_EX_TOOLWINDOW</c> / <c>WS_EX_APPWINDOW</c>。
        /// 对于原本未显示在任务栏的窗口（如设置了 ShowInTaskbar="False" 的 WPF 窗口），
        /// 同样会隐藏其 Alt+Tab 条目，恢复时不会意外将其添加到任务栏。
        /// </para>
        /// </summary>
        public bool HideFromTaskbarWhenDocked { get; set; } = true;

        /// <summary>
        /// 获取或设置鼠标释放时是否执行回弹修正。
        /// </summary>
        public bool BounceBackOnMouseUp { get; set; } = true;

        /// <summary>
        /// 获取或设置触发回弹所需的越界距离（像素）。
        /// </summary>
        public int BounceOutThresholdPixels { get; set; } = 10;

        /// <summary>
        /// 获取或设置使用弹簧动画所需的最小移动距离（像素）。
        /// </summary>
        public int SpringMinDistancePixels { get; set; } = 60;

        /// <summary>
        /// 获取或设置触发弹簧动画所需的最小越界距离（像素）。
        /// </summary>
        public int SpringMinOutPixels { get; set; } = 25;

        /// <summary>
        /// 获取或设置低频轮询检测间隔（毫秒）。
        /// <para>
        /// 作为鼠标 Hook 的兜底机制，用于处理某些 Hook 事件丢失或系统状态变化场景。
        /// </para>
        /// <para>
        /// 设为 0 可关闭轮询。
        /// </para>
        /// </summary>
        public int PollIntervalMilliseconds { get; set; } = 250;

        /// <summary>
        /// 获取或设置窗口刚被弹出或激活后，抑制自动隐藏的持续时间（毫秒）。
        /// <para>
        /// 用于避免刚唤出窗口后又立即被隐藏。
        /// </para>
        /// </summary>
        public int SuppressHideAfterActivateMilliseconds { get; set; } = 650;

        /// <summary>
        /// 获取或设置自定义窗口过滤器。
        /// <para>
        /// 返回 <see langword="true"/> 表示允许加入管理；
        /// 返回 <see langword="false"/> 表示拒绝加入管理。
        /// </para>
        /// <para>
        /// 默认情况下，引擎仅执行最小基础检查；
        /// 若调用方已在外部准确判定窗口来源，通常无需设置该过滤器。
        /// </para>
        /// <para>
        /// 若过滤器执行过程中抛出异常，则按拒绝加入管理处理。
        /// </para>
        /// </summary>
        public Predicate<IntPtr>? CustomWindowFilter { get; set; }

        internal void Normalize()
        {
            if (AnimationDurationSeconds < 0) AnimationDurationSeconds = 0;
            if (ThrottleMilliseconds <= 0) ThrottleMilliseconds = 40;
            if (EdgeTriggerPixels <= 0) EdgeTriggerPixels = 10;
            if (MouseLeaveMarginPixels < 0) MouseLeaveMarginPixels = 0;
            if (SideReleaseHysteresisPixels < 0) SideReleaseHysteresisPixels = 0;
            if (BounceOutThresholdPixels < 0) BounceOutThresholdPixels = 0;
            if (SpringMinDistancePixels < 0) SpringMinDistancePixels = 0;
            if (SpringMinOutPixels < 0) SpringMinOutPixels = 0;
            if (PollIntervalMilliseconds < 0) PollIntervalMilliseconds = 0;
            if (SuppressHideAfterActivateMilliseconds < 0) SuppressHideAfterActivateMilliseconds = 0;
        }

        internal static WindowDockOptions Clone(WindowDockOptions? options)
        {
            if (options == null)
            {
                return new WindowDockOptions();
            }

            return new WindowDockOptions
            {
                AnimationDurationSeconds = options.AnimationDurationSeconds,
                ThrottleMilliseconds = options.ThrottleMilliseconds,
                EdgeTriggerPixels = options.EdgeTriggerPixels,
                MouseLeaveMarginPixels = options.MouseLeaveMarginPixels,
                SideReleaseHysteresisPixels = options.SideReleaseHysteresisPixels,
                HideFromTaskbarWhenDocked = options.HideFromTaskbarWhenDocked,
                BounceBackOnMouseUp = options.BounceBackOnMouseUp,
                BounceOutThresholdPixels = options.BounceOutThresholdPixels,
                SpringMinDistancePixels = options.SpringMinDistancePixels,
                SpringMinOutPixels = options.SpringMinOutPixels,
                PollIntervalMilliseconds = options.PollIntervalMilliseconds,
                SuppressHideAfterActivateMilliseconds = options.SuppressHideAfterActivateMilliseconds,
                CustomWindowFilter = options.CustomWindowFilter
            };
        }
    }

    /// <summary>
    /// 表示窗口检查状态。
    /// </summary>
    public enum DockCheckStatus
    {
        /// <summary>
        /// 允许加入管理。
        /// </summary>
        Allowed = 0,

        /// <summary>
        /// 句柄为空。
        /// </summary>
        InvalidHandle,

        /// <summary>
        /// 句柄不是有效窗口。
        /// </summary>
        NotAWindow,

        /// <summary>
        /// 窗口当前已受管理。
        /// </summary>
        AlreadyManaged,

        /// <summary>
        /// 被自定义过滤器拒绝，或过滤器执行期间抛出了异常。
        /// </summary>
        RejectedByCustomFilter
    }

    /// <summary>
    /// 表示窗口加入贴边管理前的检查结果。
    /// </summary>
    public sealed class DockCheckResult
    {
        /// <summary>
        /// 获取或设置被检查的窗口句柄。
        /// </summary>
        public IntPtr Handle { get; internal set; }

        /// <summary>
        /// 获取或设置检查状态。
        /// </summary>
        public DockCheckStatus Status { get; internal set; }

        /// <summary>
        /// 获取或设置说明信息。
        /// </summary>
        public string? Message { get; internal set; }

        /// <summary>
        /// 获取一个值，表示该窗口当前是否允许加入贴边管理。
        /// </summary>
        public bool IsAllowed
        {
            get { return Status == DockCheckStatus.Allowed; }
        }
    }

    /// <summary>
    /// 表示一个对外公开的窗口贴边状态快照。
    /// <para>
    /// 该类型是只读快照，用于对外暴露某一时刻的窗口状态。
    /// 它不与引擎内部状态对象共享可变引用。
    /// </para>
    /// </summary>
    public sealed class WindowDockSnapshot
    {
        /// <summary>
        /// 获取窗口句柄。
        /// </summary>
        public IntPtr Handle { get; }

        /// <summary>
        /// 获取窗口标题。
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// 获取窗口所属进程的可执行文件路径。
        /// </summary>
        public string ProcessFilePath { get; }

        /// <summary>
        /// 获取当前推断的贴边方向。
        /// </summary>
        public DockSide Side { get; }

        /// <summary>
        /// 获取一个值，表示窗口当前是否已被贴边隐藏到屏幕外。
        /// </summary>
        public bool IsDocked { get; }

        /// <summary>
        /// 获取窗口在加入管理前是否原本显示在任务栏中。
        /// </summary>
        public bool OriginalShownOnTaskbar { get; }

        /// <summary>
        /// 获取一个值，表示窗口当前是否因引擎处理而被临时从任务栏隐藏。
        /// </summary>
        public bool TaskbarHiddenByEngine { get; }

        internal WindowDockSnapshot(
            IntPtr handle,
            string title,
            string processFilePath,
            DockSide side,
            bool isDocked,
            bool originalShownOnTaskbar,
            bool taskbarHiddenByEngine)
        {
            Handle = handle;
            Title = title ?? string.Empty;
            ProcessFilePath = processFilePath ?? string.Empty;
            Side = side;
            IsDocked = isDocked;
            OriginalShownOnTaskbar = originalShownOnTaskbar;
            TaskbarHiddenByEngine = taskbarHiddenByEngine;
        }
    }

    /// <summary>
    /// 表示一个受贴边引擎管理的窗口运行状态。
    /// <para>
    /// 该类型为引擎内部实现细节，不作为外部长期持有的数据模型使用。
    /// 对外状态通过 <see cref="WindowDockSnapshot"/> 暴露。
    /// </para>
    /// </summary>
    internal sealed class WindowDockInfo
    {
        /// <summary>
        /// 获取窗口句柄。
        /// </summary>
        public IntPtr Handle { get; private set; }

        /// <summary>
        /// 获取或设置窗口标题。
        /// <para>
        /// 标题在加入管理时初始化，后续可能由引擎内部更新。
        /// </para>
        /// </summary>
        public string Title { get; internal set; }

        /// <summary>
        /// 获取或设置窗口所属进程的可执行文件路径。
        /// <para>
        /// 某些系统进程、高权限进程或受限场景下可能无法获取，此时为空字符串。
        /// </para>
        /// </summary>
        public string ProcessFilePath { get; internal set; }

        /// <summary>
        /// 获取或设置当前推断的贴边方向。
        /// </summary>
        public DockSide Side { get; internal set; }

        /// <summary>
        /// 获取或设置一个值，表示窗口当前是否已被引擎贴边隐藏到屏幕外。
        /// </summary>
        public bool IsDocked { get; internal set; }

        /// <summary>
        /// 获取窗口在加入管理前是否原本显示在任务栏中。
        /// </summary>
        public bool OriginalShownOnTaskbar { get; private set; }

        /// <summary>
        /// 获取窗口加入管理前是否具有 WS_EX_TOOLWINDOW。
        /// <para>
        /// 用于精确恢复原始 Alt+Tab / 任务栏样式，避免 ShowInTaskbar=False 的 WPF 窗口恢复后意外出现在 Alt+Tab。
        /// </para>
        /// </summary>
        internal bool OriginalToolWindow { get; private set; }

        /// <summary>
        /// 获取窗口加入管理前是否具有 WS_EX_APPWINDOW。
        /// </summary>
        internal bool OriginalAppWindow { get; private set; }

        /// <summary>
        /// 获取或设置一个值，表示窗口当前是否因引擎处理而被临时从任务栏隐藏。
        /// </summary>
        public bool TaskbarHiddenByEngine { get; internal set; }

        internal long SuppressHideUntilTicks { get; set; }

        internal WindowDockInfo(IntPtr hwnd)
        {
            Handle = hwnd;
            Title = WindowHelper.GetWindowTitle(hwnd);
            ProcessFilePath = WindowHelper.GetProcessFilePath(WindowHelper.GetWindowProcessId(hwnd));
            Side = DockSide.None;
            IsDocked = false;
            OriginalShownOnTaskbar = WindowHelper.IsShownOnTaskbar(hwnd);
            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            OriginalToolWindow = (exStyle & WindowStyles.WS_EX_TOOLWINDOW) != 0;
            OriginalAppWindow = (exStyle & WindowStyles.WS_EX_APPWINDOW) != 0;
            TaskbarHiddenByEngine = false;
            SuppressHideUntilTicks = 0;
        }

        internal RECT GetMonitorBounds()
        {
            return WindowHelper.GetMonitorBoundsFromWindow(Handle);
        }
    }

    /// <summary>
    /// 提供窗口贴边隐藏、边缘唤出、回弹修正与任务栏状态协同的核心引擎。
    /// <para>
    /// 对大多数直接使用场景，更推荐通过 <see cref="WindowDockManager"/> 作为默认入口。
    /// </para>
    /// </summary>
    public sealed class WindowDockEngine : IDisposable
    {
        private const int BounceMarginPixels = 0;
        private const int AnimIntervalMs = 16;
        private const int MinVisiblePixelsOnNormalize = 48;

        private readonly object _sync = new object();
        private readonly List<WindowDockInfo> _windows = new(8);
        private readonly Dictionary<IntPtr, AnimState> _anims = new(8);
        private readonly List<IntPtr> _animRemove = new(8);
        private readonly SemaphoreSlim _checkSemaphore = new SemaphoreSlim(1, 1);

        private WindowDockOptions _opt;
        private Action<WindowDockSnapshot[]>? _snapshotCallback;

        private GlobalMouseHook? _mouseHook;
        private Timer? _throttleTimer;
        private Timer? _animTimer;
        private Timer? _pollTimer;

        private volatile bool _running;
        private volatile bool _mouseDown;
        private volatile bool _pendingCheck;

        private int _throttleArmed;
        private int _pendingBounce;

        private volatile int _lastMouseX;
        private volatile int _lastMouseY;
        private volatile int _hasMouse;

        private volatile bool _disposed;

        /// <summary>
        /// 初始化一个新的 <see cref="WindowDockEngine"/> 实例。
        /// </summary>
        /// <param name="options">引擎配置。传入 <see langword="null"/> 时使用默认配置。</param>
        public WindowDockEngine(WindowDockOptions? options = null)
        {
            _opt = WindowDockOptions.Clone(options);
            _opt.Normalize();
        }

        /// <summary>
        /// 获取一个值，表示当前引擎是否正在运行。
        /// </summary>
        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>
        /// 获取一个值，表示当前引擎是否已经释放。
        /// </summary>
        internal bool IsDisposed
        {
            get { return _disposed; }
        }

        /// <summary>
        /// 获取当前受管窗口数量。
        /// <para>
        /// 返回前会清理已失效的窗口句柄。
        /// </para>
        /// </summary>
        public int Count
        {
            get
            {
                lock (_sync)
                {
                    CleanupDeadWindows_NoLock();
                    return _windows.Count;
                }
            }
        }

        /// <summary>
        /// 更新引擎配置。
        /// <para>
        /// 该方法不会清空已有窗口管理状态；
        /// 若引擎正在运行，会尽量将可运行时更新的配置立即应用。
        /// </para>
        /// </summary>
        /// <param name="options">新的配置对象。传入 <see langword="null"/> 时重置为默认配置。</param>
        public void UpdateOptions(WindowDockOptions? options)
        {
            ThrowIfDisposed();

            WindowDockOptions newOptions = WindowDockOptions.Clone(options);
            newOptions.Normalize();
            Timer? oldPollTimer = null;

            lock (_sync)
            {
                _opt = newOptions;

                if (_running)
                {
                    if (_pollTimer != null)
                    {
                        if (_opt.PollIntervalMilliseconds > 0)
                        {
                            _pollTimer.Change(_opt.PollIntervalMilliseconds, _opt.PollIntervalMilliseconds);
                        }
                        else
                        {
                            oldPollTimer = _pollTimer;
                            _pollTimer = null;
                        }
                    }
                    else if (_opt.PollIntervalMilliseconds > 0)
                    {
                        _pollTimer = new Timer(OnPollTimer, null, _opt.PollIntervalMilliseconds, _opt.PollIntervalMilliseconds);
                    }
                }
            }

            DisposeTimer_NoThrow(oldPollTimer);
        }

        /// <summary>
        /// 启动引擎。
        /// <para>
        /// 该方法是幂等的；若已运行，则不会重复安装 Hook 或重复创建计时器。
        /// </para>
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                if (_running)
                    return;

                try
                {
                    _opt.Normalize();

                    _anims.Clear();
                    _animRemove.Clear();

                    _throttleTimer = new Timer(OnThrottleTimer, null, Timeout.Infinite, Timeout.Infinite);
                    _animTimer = new Timer(OnAnimTimer, null, Timeout.Infinite, Timeout.Infinite);

                    if (_opt.PollIntervalMilliseconds > 0)
                    {
                        _pollTimer = new Timer(OnPollTimer, null, _opt.PollIntervalMilliseconds, _opt.PollIntervalMilliseconds);
                    }

                    _mouseHook = new GlobalMouseHook();
                    _mouseHook.MouseRawEvent += OnMouseRawEvent;
                    _mouseHook.Install();

                    _running = true;
                }
                catch
                {
                    GlobalMouseHook? failedHook = _mouseHook;
                    Timer? failedThrottle = _throttleTimer;
                    Timer? failedAnim = _animTimer;
                    Timer? failedPoll = _pollTimer;

                    _mouseHook = null;
                    _throttleTimer = null;
                    _animTimer = null;
                    _pollTimer = null;
                    _running = false;
                    _pendingCheck = false;
                    _throttleArmed = 0;
                    _pendingBounce = 0;
                    _anims.Clear();
                    _animRemove.Clear();

                    DisposeHook_NoThrow(failedHook);
                    DisposeTimer_NoThrow(failedThrottle);
                    DisposeTimer_NoThrow(failedAnim);
                    DisposeTimer_NoThrow(failedPoll);
                    throw;
                }
            }
        }

        /// <summary>
        /// 停止引擎，恢复当前所有受管窗口的显示状态，并清空管理列表。
        /// <para>
        /// 恢复过程中会尝试将窗口重新带回用户可见层级或置于前方，
        /// 但实际前台与焦点行为仍受系统窗口管理规则限制。
        /// </para>
        /// </summary>
        public void Stop()
        {
            ThrowIfDisposed();
            StopCore();
        }

        /// <summary>
        /// 释放引擎资源。
        /// <para>
        /// 释放前会停止引擎并恢复所有受管窗口的显示状态。
        /// </para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                StopCore();
            }
            finally
            {
                try
                {
                    _checkSemaphore.Dispose();
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                }
            }
        }

        /// <summary>
        /// 检查指定窗口当前是否适合加入贴边管理。
        /// <para>
        /// 默认仅执行最小基础检查：
        /// 句柄是否为空、是否为有效窗口、是否已受管理。
        /// </para>
        /// <para>
        /// 若设置了 <see cref="WindowDockOptions.CustomWindowFilter"/>，
        /// 还会执行调用方自定义过滤规则。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要检查的窗口句柄。</param>
        /// <returns>检查结果。</returns>
        public DockCheckResult Check(IntPtr hwnd)
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                return CheckCore_NoLock(hwnd);
            }
        }

        /// <summary>
        /// 判断指定窗口当前是否适合加入贴边管理。
        /// </summary>
        /// <param name="hwnd">要检查的窗口句柄。</param>
        /// <returns>若适合加入管理，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool CanManage(IntPtr hwnd)
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                return CheckCore_NoLock(hwnd).IsAllowed;
            }
        }

        /// <summary>
        /// 尝试将指定窗口加入贴边管理。
        /// <para>
        /// 成功时若引擎尚未启动，会自动启动引擎。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要加入管理的窗口句柄。</param>
        /// <returns>加入成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool TryAdd(IntPtr hwnd)
        {
            DockCheckResult result;
            return TryAdd(hwnd, out result);
        }

        /// <summary>
        /// 尝试将指定窗口加入贴边管理，并返回检查结果。
        /// <para>
        /// 成功时若引擎尚未启动，会自动启动引擎。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要加入管理的窗口句柄。</param>
        /// <param name="result">检查结果或加入结果说明。</param>
        /// <returns>加入成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool TryAdd(IntPtr hwnd, out DockCheckResult result)
        {
            ThrowIfDisposed();

            WindowDockInfo? addedInfo = null;

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();

                result = CheckCore_NoLock(hwnd);
                if (!result.IsAllowed)
                {
                    return false;
                }

                addedInfo = Add_NoLock(hwnd);
            }

            if (addedInfo == null)
            {
                result = CreateResult(hwnd, DockCheckStatus.AlreadyManaged, "窗口已受管理。");
                return false;
            }

            try
            {
                Start();
            }
            catch
            {
                lock (_sync)
                {
                    RemoveAllByHandle_NoLock(hwnd);
                }

                throw;
            }

            NormalizeWindowOnAdd(addedInfo);
            TriggerCheckImmediate();

            result = CreateResult(hwnd, DockCheckStatus.Allowed, "窗口已加入管理。");
            return true;
        }

        /// <summary>
        /// 将指定窗口从管理中移除，并恢复其显示状态。
        /// <para>
        /// 为提升可见性，恢复后会尝试将该窗口带回用户可见层级或置于前方，
        /// 但实际前台与焦点行为仍受系统窗口管理规则限制。
        /// </para>
        /// <para>
        /// 若移除后已无受管窗口，引擎会自动停止。
        /// </para>
        /// </summary>
        /// <param name="hwnd">要移除的窗口句柄。</param>
        /// <returns>若该窗口原本受管且已成功移除，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool Remove(IntPtr hwnd)
        {
            ThrowIfDisposed();

            if (hwnd == IntPtr.Zero)
                return false;

            WindowDockInfo? removed = null;
            bool shouldStop = false;
            bool hadAny = false;

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();

                removed = FindByHandle_NoLock(hwnd);
                if (removed == null)
                    return false;

                hadAny = RemoveAllByHandle_NoLock(hwnd) > 0;
                shouldStop = _windows.Count == 0;
            }

            if (!hadAny)
                return false;

            RestoreOne(removed, true, false);

            if (shouldStop)
            {
                StopCore();
            }

            return true;
        }

        /// <summary>
        /// 切换指定窗口的贴边管理状态。
        /// <para>
        /// 若窗口当前未受管，则尝试加入管理并在成功时返回 <see langword="true"/>；
        /// 若窗口当前已受管，则将其移除并恢复显示状态，返回 <see langword="false"/>。
        /// </para>
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>
        /// <see langword="true"/> 表示调用后该窗口处于受管状态；
        /// <see langword="false"/> 表示调用后该窗口不处于受管状态。
        /// </returns>
        public bool Toggle(IntPtr hwnd)
        {
            ThrowIfDisposed();

            if (Contains(hwnd))
            {
                Remove(hwnd);
                return false;
            }

            return TryAdd(hwnd);
        }

        /// <summary>
        /// 判断指定窗口当前是否已受管。
        /// <para>
        /// 查询前会清理已失效的窗口句柄。
        /// </para>
        /// </summary>
        /// <param name="hwnd">窗口句柄。</param>
        /// <returns>若已受管，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool Contains(IntPtr hwnd)
        {
            ThrowIfDisposed();

            if (hwnd == IntPtr.Zero)
                return false;

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                return FindByHandle_NoLock(hwnd) != null;
            }
        }

        /// <summary>
        /// 清空所有受管窗口，并恢复其显示状态。
        /// <para>
        /// 该方法等价于 <see cref="Stop"/>。
        /// </para>
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            StopCore();
        }

        /// <summary>
        /// 恢复所有当前受管窗口的显示状态，但不清空管理列表。
        /// <para>
        /// 恢复过程中会尝试提升窗口可见性；
        /// 实际前台与焦点行为仍受系统窗口管理规则限制。
        /// </para>
        /// </summary>
        public void RestoreAll()
        {
            ThrowIfDisposed();
            RestoreAll(GetInfoItems_NoThrow(), true, false);
        }

        /// <summary>
        /// 获取当前受管窗口状态快照数组。
        /// <para>
        /// 返回的是只读快照，而不是引擎内部维护的运行状态对象。
        /// </para>
        /// <para>
        /// 查询前会清理已失效的窗口句柄。
        /// </para>
        /// </summary>
        /// <returns>当前受管窗口状态快照数组。</returns>
        public WindowDockSnapshot[] GetItems()
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                return CreateSnapshots_NoLock();
            }
        }

        /// <summary>
        /// 设置运行期间的状态快照回调。
        /// <para>
        /// 回调主要用于调试、监视或轻量状态展示，不保证覆盖每一次内部状态变化。
        /// </para>
        /// <para>
        /// 回调在线程池线程执行；若需要更新 UI，请调用方自行切回 UI 线程。
        /// </para>
        /// </summary>
        /// <param name="callback">快照回调。传入 <see langword="null"/> 可取消回调。</param>
        public void SetSnapshotCallback(Action<WindowDockSnapshot[]>? callback)
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                _snapshotCallback = callback;
            }
        }

        /// <summary>
        /// 立即触发一次内部检查。
        /// <para>
        /// 主要用于调试场景。
        /// </para>
        /// </summary>
        public void Tick()
        {
            ThrowIfDisposed();
            TriggerCheckImmediate();
        }

        private void StopCore()
        {
            GlobalMouseHook? hook;
            Timer? throttle;
            Timer? anim;
            Timer? poll;
            WindowDockInfo[] copy;

            lock (_sync)
            {
                if (!_running &&
                    _windows.Count == 0 &&
                    _mouseHook == null &&
                    _throttleTimer == null &&
                    _animTimer == null &&
                    _pollTimer == null)
                {
                    return;
                }

                copy = _windows.ToArray();
                _windows.Clear();

                _running = false;

                _anims.Clear();
                _animRemove.Clear();

                hook = _mouseHook;
                _mouseHook = null;

                throttle = _throttleTimer;
                _throttleTimer = null;

                anim = _animTimer;
                _animTimer = null;

                poll = _pollTimer;
                _pollTimer = null;

                _pendingCheck = false;
                _throttleArmed = 0;
                _pendingBounce = 0;
            }

            DisposeHook_NoThrow(hook);
            DisposeTimer_NoThrow(throttle);
            DisposeTimer_NoThrow(anim);
            DisposeTimer_NoThrow(poll);

            RestoreAll(copy, true, false);
            FireSnapshot(Array.Empty<WindowDockSnapshot>());
        }

        private void DisposeHook_NoThrow(GlobalMouseHook? hook)
        {
            if (hook == null)
                return;

            try
            {
                hook.MouseRawEvent -= OnMouseRawEvent;
                hook.Dispose();
            }
            catch (Exception ex)
            {
                DebugIgnore(ex);
            }
        }

        private static void DisposeTimer_NoThrow(Timer? timer)
        {
            if (timer == null)
                return;

            try
            {
                timer.Dispose();
            }
            catch (Exception ex)
            {
                DebugIgnore(ex);
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private DockCheckResult CheckCore_NoLock(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return CreateResult(hwnd, DockCheckStatus.InvalidHandle, "窗口句柄为空。");
            }

            if (!NativeMethods.IsWindow(hwnd))
            {
                return CreateResult(hwnd, DockCheckStatus.NotAWindow, "句柄不是有效窗口。");
            }

            if (WindowHelper.IsDesktopWindow(hwnd))
            {
                return CreateResult(hwnd, DockCheckStatus.RejectedByCustomFilter, "桌面相关窗口不允许加入贴边管理。");
            }

            if (FindByHandle_NoLock(hwnd) != null)
            {
                return CreateResult(hwnd, DockCheckStatus.AlreadyManaged, "窗口已受管理。");
            }

            if (_opt.CustomWindowFilter != null)
            {
                bool allowed;
                try
                {
                    allowed = _opt.CustomWindowFilter(hwnd);
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                    allowed = false;
                }

                if (!allowed)
                {
                    return CreateResult(hwnd, DockCheckStatus.RejectedByCustomFilter, "该窗口被自定义过滤器拒绝。");
                }
            }

            return CreateResult(hwnd, DockCheckStatus.Allowed, "允许加入管理。");
        }

        private static DockCheckResult CreateResult(IntPtr hwnd, DockCheckStatus status, string message)
        {
            DockCheckResult result = new DockCheckResult();
            result.Handle = hwnd;
            result.Status = status;
            result.Message = message;
            return result;
        }

        private static WindowDockSnapshot CreateSnapshot(WindowDockInfo item)
        {
            return new WindowDockSnapshot(
                item.Handle,
                item.Title,
                item.ProcessFilePath,
                item.Side,
                item.IsDocked,
                item.OriginalShownOnTaskbar,
                item.TaskbarHiddenByEngine);
        }

        private WindowDockSnapshot[] CreateSnapshots_NoLock()
        {
            if (_windows.Count == 0)
                return Array.Empty<WindowDockSnapshot>();

            WindowDockSnapshot[] result = new WindowDockSnapshot[_windows.Count];
            for (int i = 0; i < _windows.Count; i++)
            {
                result[i] = CreateSnapshot(_windows[i]);
            }

            return result;
        }

        private WindowDockInfo[] GetInfoItems_NoThrow()
        {
            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                return _windows.ToArray();
            }
        }

        private void RestoreAll(WindowDockInfo[] items, bool tryBringToFront, bool animate)
        {
            for (int i = 0; i < items.Length; i++)
            {
                RestoreOne(items[i], tryBringToFront, animate);
            }
        }

        private void RestoreOne(WindowDockInfo dw, bool tryBringToFront, bool animate)
        {
            IntPtr hwnd = dw.Handle;
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                if (!NativeMethods.IsWindow(hwnd))
                    return;

                lock (_sync)
                {
                    _anims.Remove(hwnd);
                }

                RestoreWindow(hwnd);

                RECT bounds = WindowHelper.GetWindowBounds(hwnd);
                RECT monitor = dw.GetMonitorBounds();

                bool outOfView = IsWindowOutsideMonitor(bounds, monitor);
                bool needRestoreFromEdge = dw.Side != DockSide.None && (dw.IsDocked || outOfView);

                if (needRestoreFromEdge)
                {
                    RestoreFromEdge(dw, hwnd, monitor, animate);
                }
                else
                {
                    // ↓ 修复：改用 RestoreHiddenState，正确处理原本无任务栏条目的窗口
                    if (dw.TaskbarHiddenByEngine)
                    {
                        RestoreHiddenState(hwnd, dw.OriginalToolWindow, dw.OriginalAppWindow);
                        dw.TaskbarHiddenByEngine = false;
                    }
                }

                if (tryBringToFront)
                {
                    try
                    {
                        WindowHelper.RestoreAndSetForeground(hwnd);
                    }
                    catch (Exception ex)
                    {
                        DebugIgnore(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugIgnore(ex);
            }
        }

        private void OnMouseRawEvent(MouseRawInfo e)
        {
            if (_disposed || !_running)
                return;

            _lastMouseX = e.X;
            _lastMouseY = e.Y;
            _hasMouse = 1;

            switch (e.Message)
            {
                case MouseMessage.LButtonDown:
                case MouseMessage.RButtonDown:
                case MouseMessage.MButtonDown:
                    _mouseDown = true;
                    StopThrottle();
                    break;

                case MouseMessage.LButtonUp:
                case MouseMessage.RButtonUp:
                case MouseMessage.MButtonUp:
                    _mouseDown = false;
                    if (_opt.BounceBackOnMouseUp)
                    {
                        _pendingBounce = 1;
                    }
                    TriggerCheckImmediate();
                    break;

                case MouseMessage.MouseMove:
                    if (!_mouseDown)
                    {
                        ScheduleCheck();
                    }
                    break;
            }
        }

        private void OnPollTimer(object? state)
        {
            if (_disposed || !_running)
                return;

            TriggerCheckImmediate();
        }

        private void StopThrottle()
        {
            Timer? t = _throttleTimer;
            if (t != null)
            {
                try
                {
                    t.Change(Timeout.Infinite, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // 引擎正在停止/释放，忽略
                }
            }

            _throttleArmed = 0;
        }

        private void ScheduleCheck()
        {
            _pendingCheck = true;

            if (Interlocked.Exchange(ref _throttleArmed, 1) == 0)
            {
                Timer? t = _throttleTimer;
                if (t == null)
                    return;

                try
                {
                    t.Change(_opt.ThrottleMilliseconds, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // 引擎正在停止/释放，忽略
                }
            }
        }

        private void TriggerCheckImmediate()
        {
            _pendingCheck = false;
            _throttleArmed = 0;
            ExecuteCheck();
        }

        private void OnThrottleTimer(object? state)
        {
            Interlocked.Exchange(ref _throttleArmed, 0);

            if (_disposed || !_running)
                return;

            if (!_pendingCheck)
                return;

            _pendingCheck = false;

            WindowDockSnapshot[] snap;

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                snap = CreateSnapshots_NoLock();
            }

            FireSnapshot(snap);
            ExecuteCheck();
        }

        private void FireSnapshot(WindowDockSnapshot[]? snapshot)
        {
            Action<WindowDockSnapshot[]>? cb;

            lock (_sync)
            {
                cb = _snapshotCallback;
            }

            if (cb == null)
                return;

            WindowDockSnapshot[] snap = snapshot ?? Array.Empty<WindowDockSnapshot>();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    cb(snap);
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                }
            });
        }

        private void ExecuteCheck()
        {
            // Timer 回调在 Dispose 完成后仍可能短暂运行，提前守卫避免访问已释放的信号量
            if (_disposed)
                return;

            bool entered;
            try
            {
                entered = _checkSemaphore.Wait(0);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!entered)
                return;

            bool shouldStop = false;

            try
            {
                if (!_running)
                    return;

                shouldStop = CheckRuntimeCore();
            }
            catch (Exception ex)
            {
                DebugIgnore(ex);
            }
            finally
            {
                try
                {
                    _checkSemaphore.Release();
                }
                catch (ObjectDisposedException) { }
            }

            if (shouldStop && !_disposed)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        StopCore();
                    }
                    catch (Exception ex)
                    {
                        DebugIgnore(ex);
                    }
                });
            }
        }

        private struct RectDelta
        {
            public int DxLeft;
            public int DyTop;
            public int BoundsW;
            public int BoundsH;
        }

        private static RectDelta GetRectDelta(IntPtr hwnd)
        {
            RECT wr = WindowHelper.GetWindowRect(hwnd);
            RECT br = WindowHelper.GetWindowBounds(hwnd);

            RectDelta delta = new RectDelta();
            delta.DxLeft = br.Left - wr.Left;
            delta.DyTop = br.Top - wr.Top;
            delta.BoundsW = br.Width;
            delta.BoundsH = br.Height;
            return delta;
        }

        private static long NowTicks()
        {
            return Stopwatch.GetTimestamp();
        }

        private long MsToTicks(int ms)
        {
            if (ms <= 0)
                return 0;

            return (long)((ms / 1000.0) * Stopwatch.Frequency);
        }

        private bool CheckRuntimeCore()
        {
            WindowDockInfo[] list;

            lock (_sync)
            {
                CleanupDeadWindows_NoLock();
                list = _windows.Count == 0 ? Array.Empty<WindowDockInfo>() : _windows.ToArray();
            }

            if (list.Length == 0)
                return true;

            int mx;
            int my;

            if (_hasMouse == 1)
            {
                mx = _lastMouseX;
                my = _lastMouseY;
            }
            else
            {
                POINT pt = WindowHelper.GetMousePhysicalPosition();
                mx = pt.X;
                my = pt.Y;
            }

            IntPtr fg = NativeMethods.GetForegroundWindow();
            long now = NowTicks();

            if (Interlocked.Exchange(ref _pendingBounce, 0) == 1)
            {
                BounceBackIfNeeded(list);
            }

            for (int i = 0; i < list.Length; i++)
            {
                WindowDockInfo dw = list[i];

                IntPtr hwnd = dw.Handle;
                if (hwnd == IntPtr.Zero)
                    continue;
                if (!NativeMethods.IsWindow(hwnd))
                    continue;

                RECT bounds = WindowHelper.GetWindowBounds(hwnd);
                RECT monitor = dw.GetMonitorBounds();

                if (!dw.IsDocked)
                {
                    StabilizeSide(dw, hwnd, bounds, monitor, fg);
                }

                if (dw.Side != DockSide.None && !dw.IsDocked)
                {
                    bool suppress = dw.SuppressHideUntilTicks != 0 && now < dw.SuppressHideUntilTicks;

                    if (!suppress)
                    {
                        if (!bounds.ContainsWithMargin(mx, my, _opt.MouseLeaveMarginPixels))
                        {
                            HideToEdge(dw, hwnd, bounds, monitor);
                            continue;
                        }
                    }
                }

                if (dw.Side != DockSide.None && dw.IsDocked)
                {
                    int edge = _opt.EdgeTriggerPixels;

                    if (mx >= monitor.Left && mx < monitor.Right &&
                        my >= monitor.Top && my < monitor.Bottom)
                    {
                        bool trigger = false;

                        if (dw.Side == DockSide.Top)
                        {
                            trigger = my < monitor.Top + edge &&
                                      bounds.Left - edge < mx &&
                                      mx < bounds.Right + edge;
                        }
                        else if (dw.Side == DockSide.Left)
                        {
                            trigger = mx < monitor.Left + edge &&
                                      bounds.Top - edge < my &&
                                      my < bounds.Bottom + edge;
                        }
                        else if (dw.Side == DockSide.Right)
                        {
                            trigger = mx > monitor.Right - edge &&
                                      bounds.Top - edge < my &&
                                      my < bounds.Bottom + edge;
                        }

                        if (trigger)
                        {
                            RestoreWindow(hwnd);
                            RestoreFromEdge(dw, hwnd, monitor, true);
                            dw.SuppressHideUntilTicks = now + MsToTicks(_opt.SuppressHideAfterActivateMilliseconds);

                            try
                            {
                                WindowHelper.RestoreAndSetForeground(hwnd);
                            }
                            catch (Exception ex)
                            {
                                DebugIgnore(ex);
                            }
                        }
                    }
                }
            }

            return false;
        }

        private void StabilizeSide(WindowDockInfo dw, IntPtr hwnd, RECT bounds, RECT monitor, IntPtr fg)
        {
            int edgeIn = _opt.EdgeTriggerPixels;
            int edgeOut = edgeIn + _opt.SideReleaseHysteresisPixels;
            const int fudge = 2;

            bool inTop = bounds.Top <= monitor.Top + edgeIn + fudge;
            bool inLeft = bounds.Left <= monitor.Left + edgeIn + fudge;
            bool inRight = bounds.Right >= monitor.Right - edgeIn - fudge;

            bool outTop = bounds.Top > monitor.Top + edgeOut;
            bool outLeft = bounds.Left > monitor.Left + edgeOut;
            bool outRight = bounds.Right < monitor.Right - edgeOut;

            DockSide cur = dw.Side;
            DockSide next = cur;

            if (cur == DockSide.Left)
            {
                if (outLeft) next = DockSide.None;
            }
            else if (cur == DockSide.Right)
            {
                if (outRight) next = DockSide.None;
            }
            else if (cur == DockSide.Top)
            {
                if (outTop) next = DockSide.None;
            }

            if (next == DockSide.None)
            {
                if (inTop) next = DockSide.Top;
                else if (inLeft) next = DockSide.Left;
                else if (inRight) next = DockSide.Right;
            }

            if (cur == DockSide.None && hwnd != fg)
                return;

            if (next != cur)
            {
                SetSide(hwnd, next);
            }
        }

        private void HideToEdge(WindowDockInfo dw, IntPtr hwnd, RECT bounds, RECT monitor)
        {
            RestoreWindow(hwnd);

            RectDelta d = GetRectDelta(hwnd);
            RECT wr = WindowHelper.GetWindowRect(hwnd);

            int tx = wr.Left;
            int ty = wr.Top;
            bool can = true;

            if (dw.Side == DockSide.Top)
            {
                ty = monitor.Top - d.BoundsH - d.DyTop;
            }
            else if (dw.Side == DockSide.Left)
            {
                tx = monitor.Left - d.BoundsW - d.DxLeft;
            }
            else if (dw.Side == DockSide.Right)
            {
                tx = monitor.Right - d.DxLeft;
            }
            else
            {
                can = false;
            }

            if (!can)
                return;

            StartAnimation(hwnd, wr.Left, wr.Top, tx, ty);

            if (_opt.HideFromTaskbarWhenDocked && !dw.TaskbarHiddenByEngine)
            {
                ApplyDockedHiddenState(hwnd);
                SetTaskbarHiddenByEngine(hwnd, true);
            }

            SetDocked(hwnd, true);
        }

        private void RestoreFromEdge(WindowDockInfo dw, IntPtr hwnd, RECT monitor, bool animate)
        {
            // ↓ 修复：改用 RestoreHiddenState，正确处理原本无任务栏条目的窗口
            if (dw.TaskbarHiddenByEngine)
            {
                RestoreHiddenState(hwnd, dw.OriginalToolWindow, dw.OriginalAppWindow);
                SetTaskbarHiddenByEngine(hwnd, false);
            }

            RectDelta d = GetRectDelta(hwnd);
            RECT wr = WindowHelper.GetWindowRect(hwnd);

            int tx = wr.Left;
            int ty = wr.Top;

            if (dw.Side == DockSide.Top)
            {
                ty = monitor.Top - d.DyTop + BounceMarginPixels;
            }
            else if (dw.Side == DockSide.Left)
            {
                tx = monitor.Left - d.DxLeft + BounceMarginPixels;
            }
            else if (dw.Side == DockSide.Right)
            {
                tx = monitor.Right - d.BoundsW - d.DxLeft - BounceMarginPixels;
            }

            if (animate)
            {
                StartAnimation(hwnd, wr.Left, wr.Top, tx, ty);
            }
            else
            {
                lock (_sync)
                {
                    _anims.Remove(hwnd);
                }

                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, tx, ty, 0, 0,
                    SetWindowPosFlags.SWP_NOSIZE |
                    SetWindowPosFlags.SWP_NOZORDER |
                    SetWindowPosFlags.SWP_NOSENDCHANGING);
            }

            SetDocked(hwnd, false);
        }

        private static void RestoreWindow(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, (int)ShowWindowCommands.Restore);
            }
        }

        private void BounceBackIfNeeded(WindowDockInfo[] list)
        {
            int outTh = _opt.BounceOutThresholdPixels;
            int edge = _opt.EdgeTriggerPixels;

            for (int i = 0; i < list.Length; i++)
            {
                WindowDockInfo dw = list[i];
                if (dw.IsDocked) continue;

                IntPtr hwnd = dw.Handle;
                if (hwnd == IntPtr.Zero) continue;
                if (!NativeMethods.IsWindow(hwnd)) continue;
                if (NativeMethods.IsIconic(hwnd)) continue;

                RECT br = WindowHelper.GetWindowBounds(hwnd);
                RECT monitor = dw.GetMonitorBounds();
                RectDelta d = GetRectDelta(hwnd);

                bool outLeft = br.Left < monitor.Left - outTh;
                bool outRight = br.Right > monitor.Right + outTh;
                bool outTop = br.Top < monitor.Top - outTh;

                bool nearLeft = br.Left < monitor.Left + edge;
                bool nearRight = br.Right > monitor.Right - edge;
                bool nearTop = br.Top < monitor.Top + edge;

                int targetWrLeft = int.MinValue;
                int targetWrTop = int.MinValue;
                DockSide inferred = DockSide.None;

                bool doBounce = false;
                bool doSnap = false;

                if (outLeft)
                {
                    targetWrLeft = monitor.Left - d.DxLeft + BounceMarginPixels;
                    inferred = DockSide.Left;
                    doBounce = true;
                }
                else if (outRight)
                {
                    targetWrLeft = monitor.Right - d.BoundsW - d.DxLeft - BounceMarginPixels;
                    inferred = DockSide.Right;
                    doBounce = true;
                }
                else if (outTop)
                {
                    targetWrTop = monitor.Top - d.DyTop + BounceMarginPixels;
                    inferred = DockSide.Top;
                    doBounce = true;
                }
                else
                {
                    if (nearLeft)
                    {
                        targetWrLeft = monitor.Left - d.DxLeft + BounceMarginPixels;
                        inferred = DockSide.Left;
                        doSnap = true;
                    }
                    else if (nearRight)
                    {
                        targetWrLeft = monitor.Right - d.BoundsW - d.DxLeft - BounceMarginPixels;
                        inferred = DockSide.Right;
                        doSnap = true;
                    }
                    else if (nearTop)
                    {
                        targetWrTop = monitor.Top - d.DyTop + BounceMarginPixels;
                        inferred = DockSide.Top;
                        doSnap = true;
                    }
                }

                if (!doBounce && !doSnap)
                    continue;

                if (inferred != DockSide.None)
                {
                    SetSide(hwnd, inferred);
                }

                RECT wr = WindowHelper.GetWindowRect(hwnd);

                int tx = targetWrLeft != int.MinValue ? targetWrLeft : wr.Left;
                int ty = targetWrTop != int.MinValue ? targetWrTop : wr.Top;

                int dx = Math.Abs(tx - wr.Left);
                int dy = Math.Abs(ty - wr.Top);
                int dist = dx > dy ? dx : dy;

                int outPixels = 0;
                if (outLeft) outPixels = monitor.Left - br.Left;
                else if (outRight) outPixels = br.Right - monitor.Right;
                else if (outTop) outPixels = monitor.Top - br.Top;

                if (outPixels < 0)
                {
                    outPixels = -outPixels;
                }

                bool useSpring =
                    doBounce &&
                    dist >= _opt.SpringMinDistancePixels &&
                    outPixels >= _opt.SpringMinOutPixels;

                if (useSpring)
                {
                    StartAnimation(hwnd, wr.Left, wr.Top, tx, ty,
                        0.30,
                        AnimEase.Spring,
                        16.0,
                        0.86);
                }
                else
                {
                    StartAnimation(hwnd, wr.Left, wr.Top, tx, ty,
                        0.18,
                        AnimEase.InOutQuad);
                }
            }
        }

        private enum AnimEase
        {
            InOutQuad,
            Spring
        }

        private void StartAnimation(
            IntPtr hwnd,
            int sx,
            int sy,
            int tx,
            int ty,
            double? durationSeconds = null,
            AnimEase ease = AnimEase.InOutQuad,
            double springOmega = 16.0,
            double springZeta = 0.86)
        {
            double dur;

            lock (_sync)
            {
                dur = durationSeconds ?? _opt.AnimationDurationSeconds;
            }

            if (dur <= 0 || !_running || _animTimer == null)
            {
                lock (_sync)
                {
                    _anims.Remove(hwnd);
                }

                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, tx, ty, 0, 0,
                    SetWindowPosFlags.SWP_NOSIZE |
                    SetWindowPosFlags.SWP_NOZORDER |
                    SetWindowPosFlags.SWP_NOSENDCHANGING);
                return;
            }

            bool fallbackSnap = false;

            lock (_sync)
            {
                Timer? t = _animTimer;
                if (!_running || t == null)
                {
                    _anims.Remove(hwnd);
                    fallbackSnap = true;
                }
                else
                {
                    AnimState state = new AnimState();
                    state.Hwnd = hwnd;
                    state.StartX = sx;
                    state.StartY = sy;
                    state.TargetX = tx;
                    state.TargetY = ty;
                    state.StartTicks = Stopwatch.GetTimestamp();
                    state.DurationSeconds = dur;
                    state.Ease = ease;
                    state.SpringOmega = springOmega;
                    state.SpringZeta = springZeta;

                    _anims[hwnd] = state;
                    // 持锁期间 StopCore 无法推进到 Dispose（其释放点在锁外，但置 null 在锁内），
                    // 因此此处 t 不会处于已释放状态，无需额外 try/catch
                    t.Change(0, AnimIntervalMs);
                }
            }

            if (fallbackSnap)
            {
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, tx, ty, 0, 0,
                    SetWindowPosFlags.SWP_NOSIZE |
                    SetWindowPosFlags.SWP_NOZORDER |
                    SetWindowPosFlags.SWP_NOSENDCHANGING);
            }
        }

        private void OnAnimTimer(object? state)
        {
            if (_disposed || !_running)
                return;

            if (_mouseDown)
                return;

            lock (_sync)
            {
                if (_anims.Count == 0)
                {
                    if (_animTimer != null)
                    {
                        _animTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                double freq = Stopwatch.Frequency;

                _animRemove.Clear();

                foreach (KeyValuePair<IntPtr, AnimState> kv in _anims)
                {
                    AnimState st = kv.Value;

                    if (!NativeMethods.IsWindow(st.Hwnd))
                    {
                        _animRemove.Add(st.Hwnd);
                        continue;
                    }

                    double elapsed = (now - st.StartTicks) / freq;
                    double t = elapsed / st.DurationSeconds;
                    if (t > 1) t = 1;

                    double eased = st.Ease == AnimEase.Spring
                        ? EaseSpring01(t, st.SpringOmega, st.SpringZeta)
                        : EaseInOutQuadratic(t);

                    int x = (int)(st.StartX + (st.TargetX - st.StartX) * eased);
                    int y = (int)(st.StartY + (st.TargetY - st.StartY) * eased);

                    NativeMethods.SetWindowPos(st.Hwnd, IntPtr.Zero, x, y, 0, 0,
                        SetWindowPosFlags.SWP_NOSIZE |
                        SetWindowPosFlags.SWP_NOZORDER |
                        SetWindowPosFlags.SWP_NOSENDCHANGING);

                    bool done = t >= 1.0;

                    if (!done && st.Ease == AnimEase.Spring)
                    {
                        if (Math.Abs(x - st.TargetX) <= 1 && Math.Abs(y - st.TargetY) <= 1)
                        {
                            done = true;
                        }
                    }

                    if (done)
                    {
                        NativeMethods.SetWindowPos(st.Hwnd, IntPtr.Zero, st.TargetX, st.TargetY, 0, 0,
                            SetWindowPosFlags.SWP_NOSIZE |
                            SetWindowPosFlags.SWP_NOZORDER |
                            SetWindowPosFlags.SWP_NOSENDCHANGING);

                        _animRemove.Add(st.Hwnd);
                    }
                }

                for (int i = 0; i < _animRemove.Count; i++)
                {
                    _anims.Remove(_animRemove[i]);
                }

                if (_anims.Count == 0 && _animTimer != null)
                {
                    _animTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private static double EaseSpring01(double t, double omega, double zeta)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;

            if (omega < 1) omega = 1;
            if (zeta < 0) zeta = 0;
            if (zeta > 0.99) zeta = 0.99;

            double wd = omega * Math.Sqrt(1.0 - zeta * zeta);
            double e = Math.Exp(-zeta * omega * t);

            double c = Math.Cos(wd * t);
            double s = Math.Sin(wd * t);
            double k = zeta / Math.Sqrt(1.0 - zeta * zeta);

            return 1.0 - e * (c + k * s);
        }

        private static double EaseInOutQuadratic(double t)
        {
            if (t < 0.5) return 2 * t * t;
            return -1 + (4 - 2 * t) * t;
        }

        private struct AnimState
        {
            public IntPtr Hwnd;
            public int StartX;
            public int StartY;
            public int TargetX;
            public int TargetY;
            public long StartTicks;
            public double DurationSeconds;
            public AnimEase Ease;
            public double SpringOmega;
            public double SpringZeta;
        }

        private WindowDockInfo? Add_NoLock(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            if (!NativeMethods.IsWindow(hwnd)) return null;
            if (FindByHandle_NoLock(hwnd) != null) return null;

            WindowDockInfo info = new WindowDockInfo(hwnd);

            try
            {
                RECT b = WindowHelper.GetWindowBounds(hwnd);
                RECT m = info.GetMonitorBounds();
                info.Side = GetDockSide(b, m, _opt.EdgeTriggerPixels);
            }
            catch (Exception ex)
            {
                DebugIgnore(ex);
                info.Side = DockSide.None;
            }

            _windows.Add(info);
            return info;
        }

        private WindowDockInfo? FindByHandle_NoLock(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return null;

            for (int i = 0; i < _windows.Count; i++)
            {
                if (_windows[i].Handle == hwnd)
                    return _windows[i];
            }

            return null;
        }

        private int RemoveAllByHandle_NoLock(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return 0;

            int removedCount = 0;

            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (_windows[i].Handle == hwnd)
                {
                    _windows.RemoveAt(i);
                    removedCount++;
                }
            }

            _anims.Remove(hwnd);
            return removedCount;
        }

        private void CleanupDeadWindows_NoLock()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                IntPtr hwnd = _windows[i].Handle;
                if (!NativeMethods.IsWindow(hwnd))
                {
                    _windows.RemoveAt(i);
                    _anims.Remove(hwnd);
                }
            }
        }

        private void SetDocked(IntPtr hwnd, bool docked)
        {
            lock (_sync)
            {
                WindowDockInfo? item = FindByHandle_NoLock(hwnd);
                if (item != null)
                {
                    item.IsDocked = docked;
                }
            }
        }

        private void SetSide(IntPtr hwnd, DockSide side)
        {
            lock (_sync)
            {
                WindowDockInfo? item = FindByHandle_NoLock(hwnd);
                if (item != null)
                {
                    item.Side = side;
                }
            }
        }

        private void SetTaskbarHiddenByEngine(IntPtr hwnd, bool value)
        {
            lock (_sync)
            {
                WindowDockInfo? item = FindByHandle_NoLock(hwnd);
                if (item != null)
                {
                    item.TaskbarHiddenByEngine = value;
                }
            }
        }

        private void NormalizeWindowOnAdd(WindowDockInfo dw)
        {
            IntPtr hwnd = dw.Handle;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return;
            if (NativeMethods.IsIconic(hwnd))
                return;

            RECT monitor = dw.GetMonitorBounds();
            RECT bounds = WindowHelper.GetWindowBounds(hwnd);
            RECT wr = WindowHelper.GetWindowRect(hwnd);
            RectDelta d = GetRectDelta(hwnd);

            int intersectW = GetIntersectLength(bounds.Left, bounds.Right, monitor.Left, monitor.Right);
            int intersectH = GetIntersectLength(bounds.Top, bounds.Bottom, monitor.Top, monitor.Bottom);

            bool fullyOutside = intersectW <= 0 || intersectH <= 0;
            bool barelyVisible = intersectW < MinVisiblePixelsOnNormalize || intersectH < MinVisiblePixelsOnNormalize;
            bool partialOutside =
                bounds.Left < monitor.Left ||
                bounds.Right > monitor.Right ||
                bounds.Top < monitor.Top ||
                bounds.Bottom > monitor.Bottom;

            if (!fullyOutside && !barelyVisible && !partialOutside)
            {
                // 窗口完全在屏幕内：仅推断贴边方向，通过 SetSide 走锁保证线程安全
                try
                {
                    SetSide(hwnd, GetDockSide(bounds, monitor, _opt.EdgeTriggerPixels));
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                    SetSide(hwnd, DockSide.None);
                }
                return;
            }

            int tx = wr.Left;
            int ty = wr.Top;
            DockSide side = DockSide.None;

            int leftOver = monitor.Left - bounds.Left;
            int rightOver = bounds.Right - monitor.Right;
            int topOver = monitor.Top - bounds.Top;
            int bottomOver = bounds.Bottom - monitor.Bottom;

            if (leftOver > 0 || rightOver > 0 || topOver > 0)
            {
                int best = int.MaxValue;

                if (leftOver > 0 && leftOver < best)
                {
                    best = leftOver;
                    side = DockSide.Left;
                    tx = monitor.Left - d.DxLeft + BounceMarginPixels;
                    ty = wr.Top;
                }

                if (rightOver > 0 && rightOver < best)
                {
                    best = rightOver;
                    side = DockSide.Right;
                    tx = monitor.Right - d.BoundsW - d.DxLeft - BounceMarginPixels;
                    ty = wr.Top;
                }

                if (topOver > 0 && topOver < best)
                {
                    best = topOver;
                    side = DockSide.Top;
                    tx = wr.Left;
                    ty = monitor.Top - d.DyTop + BounceMarginPixels;
                }
            }

            if (side == DockSide.None && bottomOver > 0)
            {
                ty = monitor.Bottom - d.BoundsH - d.DyTop - BounceMarginPixels;
            }

            lock (_sync)
            {
                _anims.Remove(hwnd);
            }

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, tx, ty, 0, 0,
                SetWindowPosFlags.SWP_NOSIZE |
                SetWindowPosFlags.SWP_NOZORDER |
                SetWindowPosFlags.SWP_NOSENDCHANGING);

            SetDocked(hwnd, false);

            if (side != DockSide.None)
            {
                SetSide(hwnd, side);
            }
            else
            {
                // 重新读取位置后推断贴边方向，通过 SetSide 走锁保证线程安全
                try
                {
                    RECT newBounds = WindowHelper.GetWindowBounds(hwnd);
                    SetSide(hwnd, GetDockSide(newBounds, monitor, _opt.EdgeTriggerPixels));
                }
                catch (Exception ex)
                {
                    DebugIgnore(ex);
                    SetSide(hwnd, DockSide.None);
                }
            }
        }

        internal static DockSide GetDockSide(RECT bounds, RECT monitor, int edgePixels)
        {
            const int Fudge = 2;

            if (bounds.Top <= monitor.Top + edgePixels + Fudge)
                return DockSide.Top;

            if (bounds.Left <= monitor.Left + edgePixels + Fudge)
                return DockSide.Left;

            if (bounds.Right >= monitor.Right - edgePixels - Fudge)
                return DockSide.Right;

            return DockSide.None;
        }

        /// <summary>
        /// 将窗口的 Alt+Tab / 任务栏扩展样式精确还原为加入管理前的状态。
        /// </summary>
        private static void RestoreHiddenState(IntPtr hwnd, bool originalToolWindow, bool originalAppWindow)
        {
            WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_TOOLWINDOW, originalToolWindow);
            WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_APPWINDOW, originalAppWindow);
        }

        /// <summary>
        /// 将窗口临时隐藏出 Alt+Tab / 任务栏。
        /// </summary>
        private static void ApplyDockedHiddenState(IntPtr hwnd)
        {
            WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_TOOLWINDOW, true);
            WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_APPWINDOW, false);
        }

        private static bool IsWindowOutsideMonitor(RECT bounds, RECT monitor)
        {
            return bounds.Left < monitor.Left ||
                   bounds.Right > monitor.Right ||
                   bounds.Top < monitor.Top ||
                   bounds.Bottom > monitor.Bottom;
        }

        private static int GetIntersectLength(int a1, int a2, int b1, int b2)
        {
            int start = Math.Max(a1, b1);
            int end = Math.Min(a2, b2);
            return end - start;
        }

        [Conditional("DEBUG")]
        private static void DebugIgnore(Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// 表示窗口当前所属的贴边方向。
    /// </summary>
    public enum DockSide
    {
        /// <summary>
        /// 当前不处于贴边状态。
        /// </summary>
        None = 0,

        /// <summary>
        /// 顶部贴边。
        /// </summary>
        Top = 1,

        /// <summary>
        /// 左侧贴边。
        /// </summary>
        Left = 2,

        /// <summary>
        /// 右侧贴边。
        /// </summary>
        Right = 3
    }
}
