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

        protected override void OnStartup(StartupEventArgs e)
        {
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

            _commandCancellation = new CancellationTokenSource();
            _commandListener = ListenForCommandsAsync(_commandCancellation.Token);
            _trayIcon = new TrayIconService(
                ShowCanvas,
                () =>
                {
                    HtmlWidgetCanvasWindow.DisposeWindow();
                    Shutdown();
                });
            HandleLaunchArguments(e.Args);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
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
