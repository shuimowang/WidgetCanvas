#nullable enable

using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WidgetCanvas.Services
{
    internal sealed class AppSettings
    {
        public bool AutoUpdateEnabled { get; set; } = true;

        public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Gitee;

        public bool StartWithWindows { get; set; }

        public bool HotkeyEnabled { get; set; }

        public string Hotkey { get; set; } = "Ctrl+Alt+W";

        public bool HotkeyShowsMainCanvas { get; set; }

        public DateTimeOffset? LastUpdateCheckUtc { get; set; }

        public bool WebDavAutoSyncEnabled { get; set; }

        public string WebDavUrl { get; set; } = string.Empty;

        public string WebDavUsername { get; set; } = string.Empty;

        public string WebDavProtectedPassword { get; set; } = string.Empty;

        public string WebDavDeviceId { get; set; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset? LastWebDavSyncUtc { get; set; }

        public string WebDavLastError { get; set; } = string.Empty;
    }

    internal sealed class AppSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public AppSettingsService(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            FilePath = Path.GetFullPath(filePath);
            Settings = Load();
        }

        public string FilePath { get; }

        public AppSettings Settings { get; }

        public void Save()
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("设置文件缺少目录。");

            Directory.CreateDirectory(directory);
            string tempPath = FilePath + ".tmp";
            string backupPath = FilePath + ".bak";
            byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Settings, JsonOptions));
            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
                File.Copy(FilePath, backupPath, overwrite: true);
            File.Move(tempPath, FilePath, overwrite: true);
        }

        private AppSettings Load()
        {
            foreach (string path in new[] { FilePath, FilePath + ".bak" })
            {
                try
                {
                    if (!File.Exists(path))
                        continue;
                    AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(path),
                        JsonOptions);
                    if (settings == null)
                        continue;
                    settings.Hotkey = string.IsNullOrWhiteSpace(settings.Hotkey)
                        ? "Ctrl+Alt+W"
                        : settings.Hotkey.Trim();
                    if (!Enum.IsDefined(settings.UpdateChannel))
                        settings.UpdateChannel = UpdateChannel.Gitee;
                    settings.WebDavUrl = settings.WebDavUrl?.Trim() ?? string.Empty;
                    settings.WebDavUsername = settings.WebDavUsername?.Trim() ?? string.Empty;
                    settings.WebDavProtectedPassword ??= string.Empty;
                    settings.WebDavLastError ??= string.Empty;
                    settings.WebDavDeviceId = string.IsNullOrWhiteSpace(settings.WebDavDeviceId)
                        ? Guid.NewGuid().ToString("N")
                        : settings.WebDavDeviceId.Trim();
                    return settings;
                }
                catch (JsonException)
                {
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            return new AppSettings();
        }
    }
}
