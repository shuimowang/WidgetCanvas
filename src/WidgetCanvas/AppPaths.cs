#nullable enable

using System;
using System.IO;

namespace WidgetCanvas
{
    internal static class AppPaths
    {
        private const string ProductFolderName = "浮岛";

        public static string UserDataFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ProductFolderName);

        public static string ComponentsFolder { get; } = Path.Combine(UserDataFolder, "组件");

        public static string WidgetCatalogFilePath { get; } = Path.Combine(ComponentsFolder, "widgets.json");

        public static string LocalDataFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);

        public static string WebView2DataFolder { get; } = Path.Combine(LocalDataFolder, "WebView2");

        public static string StateFolder { get; } = Path.Combine(LocalDataFolder, "State");

        public static string CanvasStateFilePath { get; } = Path.Combine(StateFolder, "canvas.json");

        public static string SettingsFolder { get; } = Path.Combine(LocalDataFolder, "Settings");

        public static string SettingsFilePath { get; } = Path.Combine(SettingsFolder, "settings.json");

        public static string UpdatesFolder { get; } = Path.Combine(LocalDataFolder, "Updates");

        public static string IntegrationFolder { get; } = Path.Combine(LocalDataFolder, "Integration");

        public static string WidgetIndexFilePath { get; } = Path.Combine(IntegrationFolder, "widgets.json");

        public static string SyncFolder { get; } = Path.Combine(LocalDataFolder, "Sync");

        public static string WebDavSyncBaseFilePath { get; } = Path.Combine(SyncFolder, "webdav-base.json");

        public static string LogsFolder { get; } = Path.Combine(LocalDataFolder, "Logs");

        public static void EnsureCreated()
        {
            Directory.CreateDirectory(ComponentsFolder);
            Directory.CreateDirectory(WebView2DataFolder);
            Directory.CreateDirectory(StateFolder);
            Directory.CreateDirectory(SettingsFolder);
            Directory.CreateDirectory(UpdatesFolder);
            Directory.CreateDirectory(IntegrationFolder);
            Directory.CreateDirectory(SyncFolder);
            Directory.CreateDirectory(LogsFolder);
        }
    }
}
