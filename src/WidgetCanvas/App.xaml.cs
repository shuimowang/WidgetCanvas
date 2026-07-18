#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WidgetCanvas.Services;
using WidgetCanvas.Windows;

namespace WidgetCanvas
{
    public partial class App : Application
    {
        private const string InstanceMutexName = "Local\\WidgetCanvas.Instance";
        private const string CommandPipeName = "WidgetCanvas.Command";

        private Mutex? _instanceMutex;
        private CancellationTokenSource? _commandCancellation;
        private Task? _commandListener;
        private TrayIconService? _trayIcon;
        private AppSettingsService? _settingsService;
        private GlobalHotkeyService? _hotkeyService;
        private UpdateService? _updateService;
        private WebDavSyncService? _webDavSyncService;
        private DispatcherTimer? _webDavPeriodicTimer;
        private DispatcherTimer? _webDavChangeTimer;
        private bool _webDavSyncInProgress;

        internal AppSettings Settings =>
            _settingsService?.Settings ?? throw new InvalidOperationException("设置服务尚未初始化。");

        protected override void OnStartup(StartupEventArgs e)
        {
            if (WidgetIntegrationCommand.IsRequest(e.Args))
            {
                base.OnStartup(e);
                Environment.ExitCode = WidgetIntegrationCommand.Execute(e.Args);
                Shutdown(Environment.ExitCode);
                return;
            }

            if (UpdateInstaller.IsApplyRequest(e.Args))
            {
                base.OnStartup(e);
                Environment.ExitCode = UpdateInstaller.Apply(e.Args);
                Shutdown(Environment.ExitCode);
                return;
            }

            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                SendArgumentsToRunningInstance(e.Args);
                Shutdown();
                return;
            }

            base.OnStartup(e);
            AppPaths.EnsureCreated();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            _settingsService = new AppSettingsService(AppPaths.SettingsFilePath);
            _updateService = new UpdateService();
            _webDavSyncService = new WebDavSyncService();
            _hotkeyService = new GlobalHotkeyService(ShowCanvas);
            if (Settings.StartWithWindows)
            {
                try
                {
                    StartupService.SetEnabled(enabled: true);
                }
                catch (Exception ex)
                {
                    WriteDiagnosticLog("startup", ex);
                }
            }
            try
            {
                ApplyHotkeySettings();
            }
            catch (Exception ex)
            {
                Settings.HotkeyEnabled = false;
                SaveSettings();
                WriteDiagnosticLog("hotkey", ex);
            }

            _commandCancellation = new CancellationTokenSource();
            _commandListener = ListenForCommandsAsync(_commandCancellation.Token);
            _trayIcon = new TrayIconService(
                ShowCanvas,
                () => HtmlWidgetCanvasWindow.ShowLibraryWindow(activate: true),
                componentName => HtmlWidgetCanvasWindow.ShowWidgetWindow(componentName, activate: true),
                HtmlWidgetCanvasWindow.GetTrayEntries,
                ShowSettings,
                () =>
                {
                    HtmlWidgetCanvasWindow.DisposeWindow();
                    Shutdown();
                });
            InitializeWebDavSync();
            HandleLaunchArguments(e.Args);
            _ = CheckForUpdatesOnStartupAsync();
            _ = SynchronizeWebDavOnStartupAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            HtmlWidgetCanvasWindow.WidgetDataSaved -= OnWidgetDataSaved;
            _webDavPeriodicTimer?.Stop();
            _webDavPeriodicTimer = null;
            _webDavChangeTimer?.Stop();
            _webDavChangeTimer = null;
            _webDavSyncService?.Dispose();
            _webDavSyncService = null;
            _updateService = null;
            _settingsService = null;
            _commandCancellation?.Cancel();
            _commandCancellation?.Dispose();
            _commandCancellation = null;
            _commandListener = null;

            if (_instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
                _instanceMutex.Dispose();
                _instanceMutex = null;
            }

            base.OnExit(e);
        }

        private void HandleLaunchArguments(string[] args)
        {
            if (args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase)))
            {
                ShowSettings();
                return;
            }

            string? componentName = GetOptionValue(args, "--widget", "-w", "/widget");
            if (!string.IsNullOrWhiteSpace(componentName))
            {
                try
                {
                    HtmlWidgetCanvasWindow.ShowWidgetWindow(componentName, activate: true);
                    return;
                }
                catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException)
                {
                    MessageBox.Show(
                        ex.Message,
                        "WidgetCanvas",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }

            if (args.Any(arg => string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase)))
                return;

            ShowCanvas();
        }

        private void ShowCanvas()
        {
            HtmlWidgetCanvasWindow canvas = HtmlWidgetCanvasWindow.ShowWindow(activate: true);
            if (MainWindow == null)
                MainWindow = canvas;
            canvas.Closed -= Canvas_Closed;
            canvas.Closed += Canvas_Closed;
        }

        private void Canvas_Closed(object? sender, EventArgs e) => Shutdown();

        private void ShowSettings() => SettingsWindow.ShowWindow(this);

        internal void SaveSettings() =>
            (_settingsService ?? throw new InvalidOperationException("设置服务尚未初始化。")).Save();

        internal void ApplyHotkeySettings() =>
            (_hotkeyService ?? throw new InvalidOperationException("快捷键服务尚未初始化。"))
                .Apply(Settings.HotkeyEnabled, Settings.Hotkey);

        internal string GetWebDavPassword() =>
            SecretProtector.Unprotect(Settings.WebDavProtectedPassword);

        internal void SaveWebDavSettings(
            bool autoSyncEnabled,
            string url,
            string username,
            string password)
        {
            string normalizedUrl = url?.Trim() ?? string.Empty;
            string normalizedUsername = username?.Trim() ?? string.Empty;
            if (autoSyncEnabled || !string.IsNullOrWhiteSpace(normalizedUrl))
                _ = WebDavSyncService.CreateOptions(normalizedUrl, normalizedUsername, password);

            bool endpointChanged = !string.Equals(
                                       Settings.WebDavUrl,
                                       normalizedUrl,
                                       StringComparison.OrdinalIgnoreCase) ||
                                   !string.Equals(
                                       Settings.WebDavUsername,
                                       normalizedUsername,
                                       StringComparison.Ordinal);
            Settings.WebDavAutoSyncEnabled = autoSyncEnabled;
            Settings.WebDavUrl = normalizedUrl;
            Settings.WebDavUsername = normalizedUsername;
            Settings.WebDavProtectedPassword = SecretProtector.Protect(password);
            if (endpointChanged)
            {
                Settings.LastWebDavSyncUtc = null;
                Settings.WebDavLastError = string.Empty;
                TryDeleteFile(AppPaths.WebDavSyncBaseFilePath);
            }
            SaveSettings();
            ApplyWebDavTimerState();
        }

        internal async Task TestWebDavAsync(
            string url,
            string username,
            string password)
        {
            WebDavSyncService service = _webDavSyncService
                ?? throw new InvalidOperationException("WebDAV 同步服务尚未初始化。");
            WebDavConnectionOptions options = WebDavSyncService.CreateOptions(url, username, password);
            await service.TestConnectionAsync(options);
        }

        internal async Task<WebDavSyncResult> SynchronizeWebDavAsync()
        {
            if (_webDavSyncInProgress)
                throw new InvalidOperationException("WebDAV 正在同步，请稍候。");
            WebDavSyncService service = _webDavSyncService
                ?? throw new InvalidOperationException("WebDAV 同步服务尚未初始化。");
            WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
                Settings.WebDavUrl,
                Settings.WebDavUsername,
                GetWebDavPassword());
            string? blockReason = HtmlWidgetCanvasWindow.GetSynchronizationBlockReason();
            if (!string.IsNullOrWhiteSpace(blockReason))
                throw new InvalidOperationException(blockReason);
            _webDavSyncInProgress = true;
            try
            {
                if (!HtmlWidgetCanvasWindow.FlushDataForSync())
                    throw new IOException("组件数据尚未成功保存，本次同步已取消。");
                WebDavSyncResult result = await service.SynchronizeAsync(
                    options,
                    Settings.WebDavDeviceId,
                    AppPaths.WidgetCatalogFilePath,
                    AppPaths.CanvasStateFilePath,
                    AppPaths.WebDavSyncBaseFilePath);
                if (result.LocalDataChanged)
                    HtmlWidgetCanvasWindow.ReloadDataAfterSync();
                Settings.LastWebDavSyncUtc = DateTimeOffset.UtcNow;
                Settings.WebDavLastError = string.Empty;
                SaveSettings();
                return result;
            }
            finally
            {
                _webDavSyncInProgress = false;
            }
        }

        internal async Task<string> CheckForUpdatesAsync(Window? owner)
        {
            UpdateService updater = _updateService
                ?? throw new InvalidOperationException("更新服务尚未初始化。");
            UpdateCheckResult update = await updater.CheckAsync();
            Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            SaveSettings();

            if (!update.IsUpdateAvailable)
                return "当前已是最新版本 · " + update.CurrentVersion.ToString(3);

            string stagedExecutable = await updater.DownloadAsync(update);
            string prompt = $"WidgetCanvas {update.LatestVersion} 已下载并通过 SHA-256 校验。\n\n立即重启并安装吗？";
            MessageBoxResult decision = owner == null
                ? MessageBox.Show(prompt, "WidgetCanvas 更新", MessageBoxButton.YesNo, MessageBoxImage.Information)
                : MessageBox.Show(owner, prompt, "WidgetCanvas 更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (decision != MessageBoxResult.Yes)
                return "新版本 " + update.LatestVersion.ToString(3) + " 已下载，稍后可再次检查并安装";

            string currentExecutable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前程序路径。");
            UpdateInstaller.Start(stagedExecutable, currentExecutable);
            HtmlWidgetCanvasWindow.DisposeWindow();
            Shutdown();
            return "正在安装更新…";
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            if (!Settings.AutoUpdateEnabled)
                return;
            if (Settings.LastUpdateCheckUtc is DateTimeOffset lastCheck &&
                DateTimeOffset.UtcNow - lastCheck < TimeSpan.FromHours(12))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
            try
            {
                await CheckForUpdatesAsync(owner: null);
            }
            catch (Exception ex)
            {
                WriteDiagnosticLog("update-check", ex);
            }
        }

        private void InitializeWebDavSync()
        {
            HtmlWidgetCanvasWindow.WidgetDataSaved += OnWidgetDataSaved;
            _webDavChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _webDavChangeTimer.Tick += WebDavChangeTimer_Tick;
            _webDavPeriodicTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _webDavPeriodicTimer.Tick += WebDavPeriodicTimer_Tick;
            ApplyWebDavTimerState();
        }

        private void ApplyWebDavTimerState()
        {
            if (_webDavPeriodicTimer == null)
                return;
            if (Settings.WebDavAutoSyncEnabled && !string.IsNullOrWhiteSpace(Settings.WebDavUrl))
                _webDavPeriodicTimer.Start();
            else
            {
                _webDavPeriodicTimer.Stop();
                _webDavChangeTimer?.Stop();
            }
        }

        private void OnWidgetDataSaved()
        {
            if (_webDavSyncInProgress ||
                !Settings.WebDavAutoSyncEnabled ||
                string.IsNullOrWhiteSpace(Settings.WebDavUrl) ||
                _webDavChangeTimer == null)
            {
                return;
            }
            _webDavChangeTimer.Stop();
            _webDavChangeTimer.Start();
        }

        private async void WebDavChangeTimer_Tick(object? sender, EventArgs e)
        {
            _webDavChangeTimer?.Stop();
            await TryAutomaticWebDavSyncAsync("webdav-change");
        }

        private async void WebDavPeriodicTimer_Tick(object? sender, EventArgs e) =>
            await TryAutomaticWebDavSyncAsync("webdav-periodic");

        private async Task SynchronizeWebDavOnStartupAsync()
        {
            if (!Settings.WebDavAutoSyncEnabled || string.IsNullOrWhiteSpace(Settings.WebDavUrl))
                return;
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (_settingsService == null)
                return;
            await TryAutomaticWebDavSyncAsync("webdav-startup");
        }

        private async Task TryAutomaticWebDavSyncAsync(string logCategory)
        {
            if (_settingsService == null)
                return;
            try
            {
                if (_webDavSyncInProgress ||
                    !Settings.WebDavAutoSyncEnabled ||
                    string.IsNullOrWhiteSpace(Settings.WebDavUrl) ||
                    HtmlWidgetCanvasWindow.GetSynchronizationBlockReason() != null)
                {
                    return;
                }
                await SynchronizeWebDavAsync();
            }
            catch (Exception ex)
            {
                if (_settingsService != null)
                {
                    Settings.WebDavLastError = ex.Message;
                    try
                    {
                        SaveSettings();
                    }
                    catch (Exception settingsException)
                    {
                        WriteDiagnosticLog("webdav-settings", settingsException);
                    }
                }
                WriteDiagnosticLog(logCategory, ex);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteDiagnosticLog("webdav-base-reset", ex);
            }
        }

        private async Task ListenForCommandsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        CommandPipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server);
                    string? json = await reader.ReadLineAsync(cancellationToken);
                    string[] args = string.IsNullOrWhiteSpace(json)
                        ? []
                        : JsonSerializer.Deserialize<string[]>(json) ?? [];
                    await Dispatcher.InvokeAsync(() => HandleLaunchArguments(args));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    WriteDiagnosticLog("ipc", ex);
                }
            }
        }

        private static void SendArgumentsToRunningInstance(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    CommandPipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                client.Connect(timeout: 3000);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(args));
            }
            catch (Exception ex)
            {
                WriteDiagnosticLog("ipc-client", ex);
            }
        }

        private static string? GetOptionValue(string[] args, params string[] names)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (!names.Any(name => string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                return index + 1 < args.Length ? args[index + 1] : string.Empty;
            }
            return null;
        }

        private static void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            WriteDiagnosticLog("crash", e.Exception);
            MessageBox.Show(
                "WidgetCanvas 遇到无法恢复的错误，详细信息已写入日志目录。",
                "WidgetCanvas",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void WriteDiagnosticLog(string category, Exception exception)
        {
            try
            {
                AppPaths.EnsureCreated();
                string logPath = Path.Combine(
                    AppPaths.LogsFolder,
                    $"{category}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
                File.WriteAllText(logPath, exception.ToString());
            }
            catch
            {
            }
        }
    }
}
