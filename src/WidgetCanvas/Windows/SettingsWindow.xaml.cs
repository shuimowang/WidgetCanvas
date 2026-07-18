#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WidgetCanvas.Services;

namespace WidgetCanvas.Windows
{
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow? _instance;
        private readonly App _app;
        private bool _initializing;

        private SettingsWindow(App app)
        {
            InitializeComponent();
            _app = app;
            Closed += (_, _) => _instance = null;
            LoadSettings();
        }

        public static void ShowWindow(App app)
        {
            ArgumentNullException.ThrowIfNull(app);
            SettingsWindow window = _instance ??= new SettingsWindow(app);
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        }

        private void LoadSettings()
        {
            _initializing = true;
            VersionText.Text = "版本 " + UpdateService.CurrentVersion.ToString(3) + " · Windows x64";
            AutoUpdateToggle.IsChecked = _app.Settings.AutoUpdateEnabled;
            StartupToggle.IsChecked = StartupService.IsEnabled();
            HotkeyToggle.IsChecked = _app.Settings.HotkeyEnabled;
            HotkeyTextBox.Text = _app.Settings.Hotkey;
            UpdateStatusText.Text = _app.Settings.LastUpdateCheckUtc is DateTimeOffset checkedAt
                ? "上次检查：" + checkedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "尚未检查更新";
            _initializing = false;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void AutoUpdateToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.AutoUpdateEnabled = AutoUpdateToggle.IsChecked == true;
            _app.SaveSettings();
        }

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            bool enabled = StartupToggle.IsChecked == true;
            try
            {
                StartupService.SetEnabled(enabled);
                _app.Settings.StartWithWindows = enabled;
                _app.SaveSettings();
            }
            catch (Exception ex)
            {
                _initializing = true;
                StartupToggle.IsChecked = !enabled;
                _initializing = false;
                ShowError(ex.Message);
            }
        }

        private void HotkeyToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            ApplyHotkey(HotkeyToggle.IsChecked == true);
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            try
            {
                HotkeyTextBox.Text = HotkeyGesture.FromInput(key, Keyboard.Modifiers);
            }
            catch (FormatException)
            {
            }
        }

        private void ApplyHotkeyButton_Click(object sender, RoutedEventArgs e) =>
            ApplyHotkey(HotkeyToggle.IsChecked == true);

        private void ApplyHotkey(bool enabled)
        {
            string previousGesture = _app.Settings.Hotkey;
            bool previousEnabled = _app.Settings.HotkeyEnabled;
            _app.Settings.Hotkey = HotkeyTextBox.Text.Trim();
            _app.Settings.HotkeyEnabled = enabled;
            try
            {
                _app.ApplyHotkeySettings();
                _app.SaveSettings();
            }
            catch (Exception ex)
            {
                _app.Settings.Hotkey = previousGesture;
                _app.Settings.HotkeyEnabled = previousEnabled;
                _app.ApplyHotkeySettings();
                _initializing = true;
                HotkeyToggle.IsChecked = previousEnabled;
                HotkeyTextBox.Text = previousGesture;
                _initializing = false;
                ShowError(ex.Message);
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "正在连接 GitHub…";
            try
            {
                UpdateStatusText.Text = await _app.CheckForUpdatesAsync(this);
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "检查失败";
                ShowError(ex.Message);
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }

        private void ProjectButton_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.ProjectUrl);

        private void ReleasesButton_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.ReleasesUrl);

        private void FeedbackButton_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.FeedbackUrl);

        private void ComponentsButton_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.ComponentsFolder);

        private void LogsButton_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogsFolder);

        private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });

        private static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        private void ShowError(string message) => MessageBox.Show(
            this,
            message,
            "WidgetCanvas",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
