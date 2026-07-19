#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetCanvas.HtmlWidgets
{
    /// <summary>
    /// 按源文件路径保存外部文件组件的窗口布局与 host.state。
    /// HTML 始终从源文件重新读取，不在这里保留副本。
    /// </summary>
    internal static class HtmlFileWidgetStateStore
    {
        private const int FormatVersion = 1;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static HtmlWidgetDefinition Load(
            string sourceFilePath,
            string html,
            string? stateFolder = null)
        {
            string fullPath = Path.GetFullPath(sourceFilePath);
            var widget = new HtmlWidgetDefinition
            {
                Id = "file-" + GetPathHash(fullPath),
                Html = html,
                Home = HtmlWidgetHome.Library,
                IsFileBacked = true,
                SourceFilePath = fullPath
            };

            try
            {
                string statePath = GetStateFilePath(fullPath, stateFolder);
                if (File.Exists(statePath))
                {
                    using var stream = new FileStream(
                        statePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    FileWidgetState? state = JsonSerializer.Deserialize<FileWidgetState>(stream, JsonOptions);
                    if (state?.FormatVersion == FormatVersion &&
                        string.Equals(state.SourceFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        widget.DetachedPosition = state.DetachedPosition ?? string.Empty;
                        widget.DetachedWidth = state.DetachedWidth;
                        widget.DetachedHeight = state.DetachedHeight;
                        widget.DetachedTopmost = state.DetachedTopmost;
                        widget.DetachedAutoHide = state.DetachedAutoHide;
                        widget.State = CloneState(state.State);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 状态损坏或暂时不可读不应阻止源 HTML 打开。
            }

            HtmlWidgetCanvasStore.Normalize(widget);
            widget.Html = html;
            widget.IsFileBacked = true;
            widget.SourceFilePath = fullPath;
            return widget;
        }

        public static void Save(HtmlWidgetDefinition widget, string? stateFolder = null)
        {
            ArgumentNullException.ThrowIfNull(widget);
            if (!widget.IsFileBacked || string.IsNullOrWhiteSpace(widget.SourceFilePath))
                return;

            string fullPath = Path.GetFullPath(widget.SourceFilePath);
            string folder = string.IsNullOrWhiteSpace(stateFolder)
                ? AppPaths.FileWidgetStateFolder
                : Path.GetFullPath(stateFolder);
            string statePath = GetStateFilePath(fullPath, folder);
            Directory.CreateDirectory(folder);
            var document = new FileWidgetState
            {
                FormatVersion = FormatVersion,
                SourceFilePath = fullPath,
                SavedAtUtc = DateTimeOffset.UtcNow,
                DetachedPosition = widget.DetachedPosition,
                DetachedWidth = widget.DetachedWidth,
                DetachedHeight = widget.DetachedHeight,
                DetachedTopmost = widget.DetachedTopmost,
                DetachedAutoHide = widget.DetachedAutoHide,
                State = CloneState(widget.State)
            };

            string tempPath = statePath + ".tmp";
            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, document, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tempPath, statePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static string GetStateFilePath(string fullPath, string? stateFolder)
        {
            string folder = string.IsNullOrWhiteSpace(stateFolder)
                ? AppPaths.FileWidgetStateFolder
                : Path.GetFullPath(stateFolder);
            return Path.Combine(folder, GetPathHash(fullPath) + ".json");
        }

        private static string GetPathHash(string fullPath)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant());
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static Dictionary<string, JsonElement> CloneState(
            Dictionary<string, JsonElement>? source)
        {
            var output = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (source == null)
                return output;
            foreach (KeyValuePair<string, JsonElement> item in source)
            {
                if (!string.IsNullOrEmpty(item.Key) && item.Value.ValueKind != JsonValueKind.Undefined)
                    output[item.Key] = item.Value.Clone();
            }
            return output;
        }

        private sealed class FileWidgetState
        {
            public int FormatVersion { get; set; }

            public string SourceFilePath { get; set; } = string.Empty;

            public DateTimeOffset SavedAtUtc { get; set; }

            public string DetachedPosition { get; set; } = string.Empty;

            public double DetachedWidth { get; set; }

            public double DetachedHeight { get; set; }

            public bool DetachedTopmost { get; set; } = true;

            public bool DetachedAutoHide { get; set; }

            public Dictionary<string, JsonElement>? State { get; set; }
        }
    }
}
