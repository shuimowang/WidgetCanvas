#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetCanvas.HtmlWidgets
{
    internal sealed class HtmlWidgetCanvasLoadResult
    {
        public required List<HtmlWidgetDefinition> Widgets { get; init; }

        public string? Notice { get; init; }

        public bool RecoveredFromBackup { get; init; }
    }

    internal static class HtmlWidgetCanvasStore
    {
        private const int ContentFormatVersion = 1;
        private const int RuntimeFormatVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string DefaultFilePath => AppPaths.WidgetCatalogFilePath;

        public static string DefaultRuntimeFilePath => AppPaths.CanvasStateFilePath;

        public static HtmlWidgetCanvasLoadResult Load(string contentFilePath, string runtimeFilePath)
        {
            var notices = new List<string>();
            bool recoveredFromBackup = false;

            ContentDocument? content = LoadDocumentWithBackup<ContentDocument>(
                contentFilePath,
                document => document.FormatVersion == ContentFormatVersion && document.Widgets != null,
                "组件数据",
                notices,
                ref recoveredFromBackup);

            if (content == null)
            {
                return new HtmlWidgetCanvasLoadResult
                {
                    Widgets = [],
                    Notice = notices.Count == 0 ? null : string.Join("；", notices),
                    RecoveredFromBackup = recoveredFromBackup
                };
            }

            RuntimeDocument? runtime = LoadDocumentWithBackup<RuntimeDocument>(
                runtimeFilePath,
                document => document.FormatVersion == RuntimeFormatVersion && document.Widgets != null,
                "运行状态",
                notices,
                ref recoveredFromBackup);
            Dictionary<string, RuntimeWidget> runtimeById = runtime?.Widgets
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item!.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last()!, StringComparer.Ordinal)
                ?? new Dictionary<string, RuntimeWidget>(StringComparer.Ordinal);

            var widgets = new List<HtmlWidgetDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentWidget? item in content.Widgets)
            {
                if (item == null)
                    continue;

                string id = string.IsNullOrWhiteSpace(item.Id)
                    ? Guid.NewGuid().ToString("N")
                    : item.Id.Trim();
                if (!ids.Add(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    ids.Add(id);
                }

                var widget = new HtmlWidgetDefinition
                {
                    Id = id,
                    Html = item.Html ?? string.Empty
                };
                if (runtimeById.TryGetValue(id, out RuntimeWidget? state))
                    ApplyRuntime(widget, state);
                Normalize(widget);
                widgets.Add(widget);
            }

            return new HtmlWidgetCanvasLoadResult
            {
                Widgets = widgets,
                Notice = notices.Count == 0 ? null : string.Join("；", notices),
                RecoveredFromBackup = recoveredFromBackup
            };
        }

        public static void Save(
            string contentFilePath,
            string runtimeFilePath,
            IReadOnlyList<HtmlWidgetDefinition> widgets)
        {
            var content = new ContentDocument
            {
                FormatVersion = ContentFormatVersion,
                SavedAtUtc = DateTimeOffset.UtcNow,
                Widgets = widgets.Select(widget => new ContentWidget
                {
                    Id = widget.Id,
                    Html = widget.Html
                }).ToList()
            };
            var runtime = new RuntimeDocument
            {
                FormatVersion = RuntimeFormatVersion,
                SavedAtUtc = DateTimeOffset.UtcNow,
                Widgets = widgets.Select(CreateRuntime).ToList()
            };

            SaveDocument(contentFilePath, content);
            SaveDocument(runtimeFilePath, runtime);
        }

        public static void Normalize(HtmlWidgetDefinition widget)
        {
            if (string.IsNullOrWhiteSpace(widget.Id))
                widget.Id = Guid.NewGuid().ToString("N");
            widget.Html ??= string.Empty;
            widget.DetachedPosition = widget.DetachedPosition?.Trim() ?? string.Empty;
            if (!Enum.IsDefined(widget.Home))
                widget.Home = HtmlWidgetHome.Canvas;
            var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (widget.State != null)
            {
                foreach (KeyValuePair<string, JsonElement> item in widget.State)
                {
                    if (!string.IsNullOrEmpty(item.Key) && item.Value.ValueKind != JsonValueKind.Undefined)
                        state[item.Key] = item.Value.Clone();
                }
            }
            widget.State = state;
            widget.Width = NormalizeSize(widget.Width, 140, 320);
            widget.Height = NormalizeSize(widget.Height, 90, 220);
            widget.X = NormalizeCoordinate(widget.X);
            widget.Y = NormalizeCoordinate(widget.Y);
        }

        private static TDocument? LoadDocumentWithBackup<TDocument>(
            string filePath,
            Func<TDocument, bool> validator,
            string displayName,
            List<string> notices,
            ref bool recoveredFromBackup)
            where TDocument : class
        {
            if (!File.Exists(filePath) && !File.Exists(GetBackupPath(filePath)))
                return null;
            if (TryReadDocument(filePath, validator, out TDocument? document))
                return document;
            if (TryReadDocument(GetBackupPath(filePath), validator, out document))
            {
                recoveredFromBackup = true;
                notices.Add(displayName + "已从上一次备份恢复");
                return document;
            }

            notices.Add(displayName + "无法读取，原文件没有被删除");
            return null;
        }

        private static bool TryReadDocument<TDocument>(
            string filePath,
            Func<TDocument, bool> validator,
            out TDocument? document)
            where TDocument : class
        {
            document = null;
            if (!File.Exists(filePath))
                return false;

            try
            {
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                document = JsonSerializer.Deserialize<TDocument>(stream, JsonOptions);
                return document != null && validator(document);
            }
            catch (JsonException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void SaveDocument<TDocument>(string filePath, TDocument document)
            where TDocument : class
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("数据文件必须包含目录", nameof(filePath));

            Directory.CreateDirectory(directory);
            string tempPath = filePath + ".tmp";
            string backupPath = GetBackupPath(filePath);
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

                if (File.Exists(filePath))
                    File.Copy(filePath, backupPath, overwrite: true);
                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void ApplyRuntime(HtmlWidgetDefinition widget, RuntimeWidget state)
        {
            widget.X = state.X;
            widget.Y = state.Y;
            widget.Width = state.Width;
            widget.Height = state.Height;
            widget.IsLocked = state.IsLocked;
            widget.Home = state.Home;
            widget.DetachedPosition = state.DetachedPosition ?? string.Empty;
            widget.DetachedTopmost = state.DetachedTopmost;
            widget.DetachedAutoHide = state.DetachedAutoHide;
            widget.State = state.State ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        private static RuntimeWidget CreateRuntime(HtmlWidgetDefinition widget) => new()
        {
            Id = widget.Id,
            X = widget.X,
            Y = widget.Y,
            Width = widget.Width,
            Height = widget.Height,
            IsLocked = widget.IsLocked,
            Home = widget.Home,
            DetachedPosition = widget.DetachedPosition,
            DetachedTopmost = widget.DetachedTopmost,
            DetachedAutoHide = widget.DetachedAutoHide,
            State = widget.State.ToDictionary(
                item => item.Key,
                item => item.Value.Clone(),
                StringComparer.Ordinal)
        };

        private static string GetBackupPath(string filePath) => filePath + ".bak";

        private static double NormalizeSize(double value, double minimum, double fallback) =>
            double.IsFinite(value) ? Math.Max(minimum, value) : fallback;

        private static double NormalizeCoordinate(double value) =>
            double.IsFinite(value) ? Math.Max(0, value) : 0;

        private sealed class ContentDocument
        {
            public int FormatVersion { get; set; }

            public DateTimeOffset SavedAtUtc { get; set; }

            public List<ContentWidget> Widgets { get; set; } = [];
        }

        private sealed class ContentWidget
        {
            public string Id { get; set; } = string.Empty;

            public string Html { get; set; } = string.Empty;
        }

        private sealed class RuntimeDocument
        {
            public int FormatVersion { get; set; }

            public DateTimeOffset SavedAtUtc { get; set; }

            public List<RuntimeWidget> Widgets { get; set; } = [];
        }

        private sealed class RuntimeWidget
        {
            public string Id { get; set; } = string.Empty;

            public double X { get; set; }

            public double Y { get; set; }

            public double Width { get; set; } = 320;

            public double Height { get; set; } = 220;

            public bool IsLocked { get; set; }

            public HtmlWidgetHome Home { get; set; } = HtmlWidgetHome.Canvas;

            public string DetachedPosition { get; set; } = string.Empty;

            public bool DetachedTopmost { get; set; } = true;

            public bool DetachedAutoHide { get; set; }

            public Dictionary<string, JsonElement>? State { get; set; }
        }
    }
}
