#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using WidgetCanvas.HtmlWidgets;

namespace WidgetCanvas.Services
{
    internal sealed record WidgetCatalogEntry(string Id, string Title, HtmlWidgetHome Home);

    internal sealed class WidgetCatalogSnapshot
    {
        public int SchemaVersion { get; init; } = 1;

        public long Revision { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        public IReadOnlyList<string> Titles { get; init; } = [];

        public IReadOnlyList<WidgetCatalogEntry> Components { get; init; } = [];
    }

    internal sealed class WidgetCatalogIntegration : IDisposable
    {
        public const string ChangeEventName = @"Local\WidgetCanvas.ComponentsChanged";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private readonly string _snapshotPath;
        private readonly EventWaitHandle _changeEvent;
        private string? _fingerprint;
        private long _revision;

        public WidgetCatalogIntegration(
            string snapshotPath,
            string eventName = ChangeEventName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
            _snapshotPath = Path.GetFullPath(snapshotPath);
            _changeEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                eventName);
            TryLoadExistingSnapshot();
        }

        public bool PublishIfChanged(IEnumerable<WidgetCatalogEntry> components)
        {
            WidgetCatalogEntry[] entries = Normalize(components);
            string fingerprint = JsonSerializer.Serialize(entries, JsonOptions);
            if (string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
                return false;

            var snapshot = new WidgetCatalogSnapshot
            {
                Revision = checked(_revision + 1),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Titles = entries.Select(entry => entry.Title).ToArray(),
                Components = entries
            };
            WriteSnapshot(_snapshotPath, snapshot);
            _fingerprint = fingerprint;
            _revision = snapshot.Revision;
            _changeEvent.Set();
            return true;
        }

        public void Dispose() => _changeEvent.Dispose();

        public static WidgetCatalogSnapshot CreateSnapshotFromStore(
            string contentFilePath,
            string runtimeFilePath)
        {
            HtmlWidgetCanvasLoadResult loadResult = HtmlWidgetCanvasStore.Load(
                contentFilePath,
                runtimeFilePath);
            WidgetCatalogEntry[] entries = Normalize(loadResult.Widgets.Select(widget =>
                new WidgetCatalogEntry(
                    widget.Id,
                    HtmlWidgetTitle.GetDisplayName(widget),
                    widget.Home)));
            return new WidgetCatalogSnapshot
            {
                Revision = 0,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Titles = entries.Select(entry => entry.Title).ToArray(),
                Components = entries
            };
        }

        public static string Serialize(WidgetCatalogSnapshot snapshot) =>
            JsonSerializer.Serialize(snapshot, JsonOptions);

        private static WidgetCatalogEntry[] Normalize(IEnumerable<WidgetCatalogEntry> components) =>
            components
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Title))
                .Select(entry => entry with
                {
                    Id = entry.Id?.Trim() ?? string.Empty,
                    Title = entry.Title.Trim()
                })
                .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .ToArray();

        private void TryLoadExistingSnapshot()
        {
            try
            {
                if (!File.Exists(_snapshotPath))
                    return;
                WidgetCatalogSnapshot? snapshot = JsonSerializer.Deserialize<WidgetCatalogSnapshot>(
                    File.ReadAllText(_snapshotPath),
                    JsonOptions);
                if (snapshot == null || snapshot.SchemaVersion != 1)
                    return;
                WidgetCatalogEntry[] entries = Normalize(snapshot.Components);
                _fingerprint = JsonSerializer.Serialize(entries, JsonOptions);
                _revision = Math.Max(0, snapshot.Revision);
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

        private static void WriteSnapshot(string path, WidgetCatalogSnapshot snapshot)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("组件索引文件缺少目录。");
            Directory.CreateDirectory(directory);
            string tempPath = path + ".tmp";
            byte[] json = Encoding.UTF8.GetBytes(Serialize(snapshot));
            try
            {
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
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }

    internal static class WidgetIntegrationCommand
    {
        private const string ListWidgetsOption = "--list-widgets";

        public static bool IsRequest(string[] args) =>
            args.Any(arg => string.Equals(arg, ListWidgetsOption, StringComparison.OrdinalIgnoreCase));

        public static int Execute(string[] args)
        {
            try
            {
                WidgetCatalogSnapshot snapshot = WidgetCatalogIntegration.CreateSnapshotFromStore(
                    AppPaths.WidgetCatalogFilePath,
                    AppPaths.CanvasStateFilePath);
                string json = WidgetCatalogIntegration.Serialize(snapshot);
                string? outputPath = GetOptionValue(args, "--output", "-o");
                bool outputRequested = args.Any(arg =>
                    string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase));
                if (outputRequested && string.IsNullOrWhiteSpace(outputPath))
                    throw new ArgumentException("--output 后必须提供 JSON 文件路径。");
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    string fullPath = Path.GetFullPath(outputPath);
                    string? directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllText(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                else
                {
                    using Stream output = Console.OpenStandardOutput();
                    using var writer = new StreamWriter(
                        output,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        leaveOpen: false);
                    writer.WriteLine(json);
                }
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    using Stream error = Console.OpenStandardError();
                    using var writer = new StreamWriter(
                        error,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        leaveOpen: false);
                    writer.WriteLine(ex.Message);
                }
                catch
                {
                }
                return 2;
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
    }
}
