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
            GiteeUpdateRadio.IsChecked = _app.Settings.UpdateChannel == UpdateChannel.Gitee;
            GitHubUpdateRadio.IsChecked = _app.Settings.UpdateChannel == UpdateChannel.GitHub;
            StartupToggle.IsChecked = StartupService.IsEnabled();
            HotkeyToggle.IsChecked = _app.Settings.HotkeyEnabled;
            HotkeyTextBox.Text = _app.Settings.Hotkey;
            WebDavAutoSyncToggle.IsChecked = _app.Settings.WebDavAutoSyncEnabled;
            WebDavUrlTextBox.Text = _app.Settings.WebDavUrl;
            WebDavUsernameTextBox.Text = _app.Settings.WebDavUsername;
            try
            {
                WebDavPasswordBox.Password = _app.GetWebDavPassword();
                WebDavStatusText.Text = !string.IsNullOrWhiteSpace(_app.Settings.WebDavLastError)
                    ? "上次同步失败：" + _app.Settings.WebDavLastError
                    : _app.Settings.LastWebDavSyncUtc is DateTimeOffset syncedAt
                        ? "上次同步：" + syncedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : "尚未同步";
            }
            catch (InvalidOperationException ex)
            {
                WebDavPasswordBox.Clear();
                WebDavStatusText.Text = ex.Message;
            }
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

        private void SettingsSection_Changed(object sender, RoutedEventArgs e)
        {
            if (GeneralPage == null || SyncPage == null || UpdatePage == null || AboutPage == null)
                return;
            string section = (sender as FrameworkElement)?.Tag as string ?? "General";
            GeneralPage.Visibility = section == "General" ? Visibility.Visible : Visibility.Collapsed;
            SyncPage.Visibility = section == "Sync" ? Visibility.Visible : Visibility.Collapsed;
            UpdatePage.Visibility = section == "Update" ? Visibility.Visible : Visibility.Collapsed;
            AboutPage.Visibility = section == "About" ? Visibility.Visible : Visibility.Collapsed;
            if (section == "General")
                GeneralPage.ScrollToTop();
            else if (section == "Sync")
                SyncPage.ScrollToTop();
            else if (section == "Update")
                UpdatePage.ScrollToTop();
            else
                AboutPage.ScrollToTop();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void AutoUpdateToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;
            _app.Settings.AutoUpdateEnabled = AutoUpdateToggle.IsChecked == true;
            _app.SaveSettings();
        }

        private void UpdateChannelRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing || GiteeUpdateRadio == null)
                return;
            _app.Settings.UpdateChannel = GiteeUpdateRadio.IsChecked == true
                ? UpdateChannel.Gitee
                : UpdateChannel.GitHub;
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
            UpdateStatusText.Text = "正在连接 " +
                UpdateService.GetChannelName(_app.Settings.UpdateChannel) + "…";
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

        private async void TestWebDavButton_Click(object sender, RoutedEventArgs e)
        {
            SetWebDavButtonsEnabled(false);
            WebDavStatusText.Text = "正在测试 WebDAV…";
            try
            {
                await _app.TestWebDavAsync(
                    WebDavUrlTextBox.Text,
                    WebDavUsernameTextBox.Text,
                    WebDavPasswordBox.Password);
                WebDavStatusText.Text = "连接成功 · 目录可访问";
            }
            catch (Exception ex)
            {
                WebDavStatusText.Text = "连接失败";
                ShowError(ex.Message);
            }
            finally
            {
                SetWebDavButtonsEnabled(true);
            }
        }

        private void SaveWebDavButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveCurrentWebDavSettings();
                WebDavStatusText.Text = "WebDAV 设置已保存";
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private async void SyncWebDavButton_Click(object sender, RoutedEventArgs e)
        {
            SetWebDavButtonsEnabled(false);
            try
            {
                SaveCurrentWebDavSettings();
                WebDavStatusText.Text = "正在同步组件…";
                WebDavSyncResult result = await _app.SynchronizeWebDavAsync();
                WebDavStatusText.Text = result.Message + $" · {result.WidgetCount} 个组件";
            }
            catch (Exception ex)
            {
                WebDavStatusText.Text = "同步失败";
                ShowError(ex.Message);
            }
            finally
            {
                SetWebDavButtonsEnabled(true);
            }
        }

        private void SaveCurrentWebDavSettings() => _app.SaveWebDavSettings(
            WebDavAutoSyncToggle.IsChecked == true,
            WebDavUrlTextBox.Text,
            WebDavUsernameTextBox.Text,
            WebDavPasswordBox.Password);

        private void SetWebDavButtonsEnabled(bool enabled)
        {
            TestWebDavButton.IsEnabled = enabled;
            SaveWebDavButton.IsEnabled = enabled;
            SyncWebDavButton.IsEnabled = enabled;
        }

        private void ProjectButton_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.ProjectUrl);

        private void GiteeProjectButton_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.GiteeProjectUrl);

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
