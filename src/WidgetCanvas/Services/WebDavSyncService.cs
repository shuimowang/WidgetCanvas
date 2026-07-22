#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WidgetCanvas.HtmlWidgets;

namespace WidgetCanvas.Services
{
    internal sealed record WebDavConnectionOptions(
        Uri ParentDirectoryUri,
        Uri DirectoryUri,
        Uri FileUri,
        string Username,
        string Password);

    internal sealed record WebDavSyncResult(
        bool LocalDataChanged,
        bool RemoteDataChanged,
        int WidgetCount,
        int ConflictCount,
        string Message);

    internal sealed class WebDavSyncDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public string DeviceId { get; set; } = string.Empty;

        public List<WebDavSyncWidget> Widgets { get; set; } = [];
    }

    internal sealed class WebDavSyncWidget
    {
        public string Id { get; set; } = string.Empty;

        public string Html { get; set; } = string.Empty;

        public Dictionary<string, JsonElement> State { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed record WebDavMergeResult(
        IReadOnlyList<HtmlWidgetDefinition> Widgets,
        bool LocalDataChanged,
        int ConflictCount);

    /// <summary>
    /// WebDAV 上只保存可跨设备使用的数据：组件 HTML 与组件自身状态。
    /// 画布坐标、独立窗口位置、置顶和贴边等设备状态始终保留在本机。
    /// </summary>
    internal sealed class WebDavSyncService : IDisposable
    {
        internal const string RemoteFileName = "widgetcanvas-sync-v1.json";
        internal const string RemoteDirectoryName = "WidgetCanvas";
        private const int MaximumRemoteBytes = 64 * 1024 * 1024;
        private const int MaximumWidgetCount = 10_000;
        private const int MaximumWidgetHtmlBytes = 2 * 1024 * 1024;
        private static readonly HttpMethod PropFindMethod = new("PROPFIND");
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        public WebDavSyncService()
            : this(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = false,
                UseCookies = false
            })
        {
        }

        internal WebDavSyncService(HttpMessageHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        public static WebDavConnectionOptions CreateOptions(
            string url,
            string? username,
            string? password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? directory) ||
                directory.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException("WebDAV 地址必须是完整的 HTTP 或 HTTPS 目录地址。", nameof(url));
            }
            if (!string.IsNullOrEmpty(directory.Query) || !string.IsNullOrEmpty(directory.Fragment))
                throw new ArgumentException("WebDAV 目录地址不能包含查询参数或片段。", nameof(url));

            string directoryText = directory.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? directory.AbsoluteUri
                : directory.AbsoluteUri + "/";
            Uri configuredDirectory = new(directoryText, UriKind.Absolute);
            string lastSegment = Uri.UnescapeDataString(
                configuredDirectory.Segments[^1].TrimEnd('/'));
            bool pointsToManagedDirectory = lastSegment.Equals(
                RemoteDirectoryName,
                StringComparison.OrdinalIgnoreCase);
            Uri parentDirectory = pointsToManagedDirectory
                ? new Uri(configuredDirectory, "../")
                : configuredDirectory;
            Uri syncDirectory = pointsToManagedDirectory
                ? configuredDirectory
                : new Uri(configuredDirectory, RemoteDirectoryName + "/");
            return new WebDavConnectionOptions(
                parentDirectory,
                syncDirectory,
                new Uri(syncDirectory, RemoteFileName),
                username?.Trim() ?? string.Empty,
                password ?? string.Empty);
        }

        public async Task TestConnectionAsync(
            WebDavConnectionOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            await RequireDirectoryAsync(options.ParentDirectoryUri, options, cancellationToken);
            await EnsureSyncDirectoryAsync(options, cancellationToken);
        }

        public async Task<WebDavSyncResult> SynchronizeAsync(
            WebDavConnectionOptions options,
            string deviceId,
            string contentFilePath,
            string runtimeFilePath,
            string baseFilePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
            await _syncGate.WaitAsync(cancellationToken);
            try
            {
                await EnsureSyncDirectoryAsync(options, cancellationToken);
                HtmlWidgetCanvasLoadResult localLoad = HtmlWidgetCanvasStore.Load(
                    contentFilePath,
                    runtimeFilePath);
                List<HtmlWidgetDefinition> localWidgets = localLoad.Widgets;
                WebDavSyncDocument baseDocument = LoadDocument(baseFilePath) ?? CreateEmptyDocument(deviceId);
                RemoteDocument? remote = await DownloadAsync(options, cancellationToken);

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    WebDavSyncDocument remoteDocument = remote?.Document ?? CreateEmptyDocument(deviceId);
                    WebDavMergeResult merge = WebDavSyncMerger.Merge(
                        localWidgets,
                        remoteDocument,
                        baseDocument);
                    WebDavSyncDocument mergedDocument = CreateDocument(merge.Widgets, deviceId);
                    bool remoteChanged = remote == null ||
                        !WebDavSyncMerger.DocumentsEquivalent(remoteDocument, mergedDocument);

                    if (remoteChanged)
                    {
                        try
                        {
                            await UploadAsync(
                                options,
                                mergedDocument,
                                remote != null,
                                remote?.ETag,
                                cancellationToken);
                        }
                        catch (WebDavConcurrentChangeException) when (attempt == 0)
                        {
                            remote = await DownloadAsync(options, cancellationToken);
                            continue;
                        }
                    }

                    if (merge.LocalDataChanged)
                    {
                        HtmlWidgetCanvasStore.Save(
                            contentFilePath,
                            runtimeFilePath,
                            merge.Widgets,
                            localLoad.Canvases,
                            localLoad.ActiveCanvasId);
                    }
                    SaveDocument(baseFilePath, mergedDocument);

                    string message = merge.ConflictCount > 0
                        ? $"同步完成 · 保留了 {merge.ConflictCount} 个来自其他设备的冲突副本"
                        : merge.LocalDataChanged && remoteChanged
                            ? "同步完成 · 已合并本机与远端更改"
                            : merge.LocalDataChanged
                                ? "同步完成 · 已获取远端更改"
                                : remoteChanged
                                    ? "同步完成 · 已上传本机更改"
                                    : "同步完成 · 数据已是最新";
                    return new WebDavSyncResult(
                        merge.LocalDataChanged,
                        remoteChanged,
                        merge.Widgets.Count,
                        merge.ConflictCount,
                        message);
                }

                throw new InvalidOperationException("远端数据正在被其他设备持续修改，请稍后重试。");
            }
            finally
            {
                _syncGate.Release();
            }
        }

        internal static WebDavSyncDocument CreateDocument(
            IReadOnlyList<HtmlWidgetDefinition> widgets,
            string deviceId) => new()
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DeviceId = deviceId,
            Widgets = widgets
                .Select(WebDavSyncMerger.CreatePortableWidget)
                .OrderBy(widget => widget.Id, StringComparer.Ordinal)
                .ToList()
        };

        private async Task<RemoteDocument?> DownloadAsync(
            WebDavConnectionOptions options,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, options.FileUri, options);
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw CreateWebDavException(response.StatusCode, testingDirectory: false);

            byte[] bytes = await ReadLimitedAsync(response.Content, MaximumRemoteBytes, cancellationToken);
            WebDavSyncDocument document;
            try
            {
                document = JsonSerializer.Deserialize<WebDavSyncDocument>(bytes, JsonOptions)
                    ?? throw new JsonException("同步文件为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("WebDAV 上的 WidgetCanvas 同步文件格式无效，远端文件没有被覆盖。", ex);
            }
            ValidateDocument(document);
            return new RemoteDocument(document, response.Headers.ETag?.ToString());
        }

        private async Task EnsureSyncDirectoryAsync(
            WebDavConnectionOptions options,
            CancellationToken cancellationToken)
        {
            HttpStatusCode status = await ProbeDirectoryAsync(
                options.DirectoryUri,
                options,
                cancellationToken);
            if ((int)status is >= 200 and <= 299)
                return;
            if (status != HttpStatusCode.NotFound)
                throw CreateWebDavException(status, testingDirectory: true);

            await RequireDirectoryAsync(options.ParentDirectoryUri, options, cancellationToken);
            Uri creationUri = new(options.DirectoryUri.AbsoluteUri.TrimEnd('/'), UriKind.Absolute);
            using HttpRequestMessage request = CreateRequest(
                new HttpMethod("MKCOL"),
                creationUri,
                options);
            request.Content = new ByteArrayContent([]);
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return;

            // 另一台设备可能刚好已经创建了目录；部分服务此时返回 405。
            if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                status = await ProbeDirectoryAsync(options.DirectoryUri, options, cancellationToken);
                if ((int)status is >= 200 and <= 299)
                    return;
            }

            throw response.StatusCode == HttpStatusCode.Conflict
                ? new InvalidOperationException("无法创建 WidgetCanvas WebDAV 同步目录，请确认上级目录存在且允许创建文件夹。")
                : CreateWebDavException(response.StatusCode, testingDirectory: true);
        }

        private async Task RequireDirectoryAsync(
            Uri directoryUri,
            WebDavConnectionOptions options,
            CancellationToken cancellationToken)
        {
            HttpStatusCode status = await ProbeDirectoryAsync(directoryUri, options, cancellationToken);
            if ((int)status is >= 200 and <= 299)
                return;
            throw CreateWebDavException(status, testingDirectory: true);
        }

        private async Task<HttpStatusCode> ProbeDirectoryAsync(
            Uri directoryUri,
            WebDavConnectionOptions options,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = CreateRequest(PropFindMethod, directoryUri, options);
            request.Headers.TryAddWithoutValidation("Depth", "0");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><propfind xmlns=\"DAV:\"><prop><resourcetype/></prop></propfind>",
                Encoding.UTF8,
                "application/xml");
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            return response.StatusCode;
        }

        private async Task UploadAsync(
            WebDavConnectionOptions options,
            WebDavSyncDocument document,
            bool remoteExists,
            string? etag,
            CancellationToken cancellationToken)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            using HttpRequestMessage request = CreateRequest(HttpMethod.Put, options.FileUri, options);
            request.Content = new ByteArrayContent(json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
            if (!remoteExists)
                request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            else if (!string.IsNullOrWhiteSpace(etag))
                request.Headers.TryAddWithoutValidation("If-Match", etag);

            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                throw new WebDavConcurrentChangeException();
            if (!response.IsSuccessStatusCode)
                throw CreateWebDavException(response.StatusCode, testingDirectory: false);
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                return await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("连接 WebDAV 超时，请检查服务器地址和网络。");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException("无法连接 WebDAV：" + ex.Message, ex);
            }
        }

        private static HttpRequestMessage CreateRequest(
            HttpMethod method,
            Uri uri,
            WebDavConnectionOptions options)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.UserAgent.ParseAdd(
                "WidgetCanvas/" + UpdateService.CurrentVersion.ToString(3));
            if (!string.IsNullOrEmpty(options.Username) || !string.IsNullOrEmpty(options.Password))
            {
                string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    options.Username + ":" + options.Password));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return request;
        }

        private static async Task<byte[]> ReadLimitedAsync(
            HttpContent content,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength is long length && length > maximumBytes)
                throw new InvalidDataException("WebDAV 同步文件超过 64 MB，已拒绝读取。");
            await using Stream input = await content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            byte[] buffer = new byte[32 * 1024];
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                if (output.Length + read > maximumBytes)
                    throw new InvalidDataException("WebDAV 同步文件超过 64 MB，已拒绝读取。");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        private static void ValidateDocument(WebDavSyncDocument document)
        {
            if (document.SchemaVersion != 1 || document.Widgets == null)
                throw new InvalidDataException("WebDAV 同步文件版本不受支持，远端文件没有被覆盖。");
            if (document.Widgets.Count > MaximumWidgetCount)
                throw new InvalidDataException("WebDAV 同步文件中的组件数量异常，已拒绝读取。");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WebDavSyncWidget? widget in document.Widgets)
            {
                if (widget == null || string.IsNullOrWhiteSpace(widget.Id))
                    throw new InvalidDataException("WebDAV 同步文件包含无效或重复的组件 ID。");
                widget.Id = widget.Id.Trim();
                if (!ids.Add(widget.Id))
                    throw new InvalidDataException("WebDAV 同步文件包含无效或重复的组件 ID。");
                widget.Html ??= string.Empty;
                widget.State ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (Encoding.UTF8.GetByteCount(widget.Html) > MaximumWidgetHtmlBytes)
                    throw new InvalidDataException("WebDAV 同步文件中有组件超过 2 MB，已拒绝读取。");
                if (string.IsNullOrWhiteSpace(HtmlWidgetTitle.GetHtmlTitle(widget.Html)))
                    throw new InvalidDataException("WebDAV 同步文件中有组件缺少有效 title，已拒绝读取。");
            }
        }

        private static WebDavSyncDocument? LoadDocument(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                WebDavSyncDocument? document = JsonSerializer.Deserialize<WebDavSyncDocument>(
                    File.ReadAllBytes(path),
                    JsonOptions);
                if (document == null || document.SchemaVersion != 1 || document.Widgets == null)
                    return null;
                ValidateDocument(document);
                return document;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return null;
            }
        }

        private static void SaveDocument(string path, WebDavSyncDocument document)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("WebDAV 同步基线文件缺少目录。");
            Directory.CreateDirectory(directory);
            string tempPath = path + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static WebDavSyncDocument CreateEmptyDocument(string deviceId) => new()
        {
            DeviceId = deviceId,
            Widgets = []
        };

        private static Exception CreateWebDavException(HttpStatusCode statusCode, bool testingDirectory)
        {
            return statusCode switch
            {
                HttpStatusCode.Unauthorized => new InvalidOperationException("WebDAV 身份验证失败，请检查用户名和密码。"),
                HttpStatusCode.Forbidden => new InvalidOperationException("WebDAV 拒绝访问，请检查账号权限。"),
                HttpStatusCode.NotFound when testingDirectory => new InvalidOperationException("WebDAV 目录不存在，请先创建目录或修改地址。"),
                HttpStatusCode.MethodNotAllowed when testingDirectory => new InvalidOperationException("服务器不支持 WebDAV PROPFIND，请确认填写的是 WebDAV 目录地址。"),
                _ => new InvalidOperationException($"WebDAV 请求失败：HTTP {(int)statusCode} {statusCode}")
            };
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _syncGate.Dispose();
        }

        private sealed record RemoteDocument(WebDavSyncDocument Document, string? ETag);

        private sealed class WebDavConcurrentChangeException : Exception
        {
        }
    }

    internal static class WebDavSyncMerger
    {
        public static WebDavMergeResult Merge(
            IReadOnlyList<HtmlWidgetDefinition> localWidgets,
            WebDavSyncDocument remoteDocument,
            WebDavSyncDocument baseDocument)
        {
            ArgumentNullException.ThrowIfNull(localWidgets);
            ArgumentNullException.ThrowIfNull(remoteDocument);
            ArgumentNullException.ThrowIfNull(baseDocument);

            Dictionary<string, WebDavSyncWidget> remote = ToMap(remoteDocument.Widgets);
            Dictionary<string, WebDavSyncWidget> baseline = ToMap(baseDocument.Widgets);
            var output = new List<HtmlWidgetDefinition>();
            var handled = new HashSet<string>(StringComparer.Ordinal);
            int conflicts = 0;

            foreach (HtmlWidgetDefinition local in localWidgets)
            {
                if (!handled.Add(local.Id))
                    continue;
                WebDavSyncWidget localPortable = CreatePortableWidget(local);
                remote.TryGetValue(local.Id, out WebDavSyncWidget? remoteWidget);
                baseline.TryGetValue(local.Id, out WebDavSyncWidget? baseWidget);

                if (baseWidget == null)
                {
                    output.Add(CloneDefinition(local));
                    if (remoteWidget != null && !WidgetsEquivalent(localPortable, remoteWidget))
                    {
                        output.Add(CreateImported(remoteWidget, conflictCopy: true));
                        conflicts++;
                    }
                    continue;
                }

                bool localChanged = !WidgetsEquivalent(localPortable, baseWidget);
                if (remoteWidget == null)
                {
                    if (localChanged)
                        output.Add(CloneDefinition(local));
                    continue;
                }

                bool remoteChanged = !WidgetsEquivalent(remoteWidget, baseWidget);
                if (!localChanged && remoteChanged)
                {
                    output.Add(ApplyPortable(local, remoteWidget));
                }
                else if (localChanged && remoteChanged &&
                         !WidgetsEquivalent(localPortable, remoteWidget))
                {
                    output.Add(CloneDefinition(local));
                    output.Add(CreateImported(remoteWidget, conflictCopy: true));
                    conflicts++;
                }
                else
                {
                    output.Add(CloneDefinition(local));
                }
            }

            foreach (WebDavSyncWidget remoteWidget in remoteDocument.Widgets)
            {
                if (!handled.Add(remoteWidget.Id))
                    continue;
                baseline.TryGetValue(remoteWidget.Id, out WebDavSyncWidget? baseWidget);
                if (baseWidget != null && WidgetsEquivalent(remoteWidget, baseWidget))
                    continue;
                output.Add(CreateImported(remoteWidget, conflictCopy: false));
            }

            EnsureUniqueTitles(output);
            bool localDataChanged = !DocumentsEquivalent(
                WebDavSyncService.CreateDocument(localWidgets, string.Empty),
                WebDavSyncService.CreateDocument(output, string.Empty));
            return new WebDavMergeResult(output, localDataChanged, conflicts);
        }

        public static bool DocumentsEquivalent(
            WebDavSyncDocument left,
            WebDavSyncDocument right)
        {
            Dictionary<string, WebDavSyncWidget> leftMap = ToMap(left.Widgets);
            Dictionary<string, WebDavSyncWidget> rightMap = ToMap(right.Widgets);
            if (leftMap.Count != rightMap.Count)
                return false;
            return leftMap.All(item =>
                rightMap.TryGetValue(item.Key, out WebDavSyncWidget? other) &&
                WidgetsEquivalent(item.Value, other));
        }

        internal static WebDavSyncWidget CreatePortableWidget(HtmlWidgetDefinition widget) => new()
        {
            Id = widget.Id,
            Html = widget.Html,
            State = CloneState(widget.State)
        };

        private static Dictionary<string, WebDavSyncWidget> ToMap(
            IEnumerable<WebDavSyncWidget>? widgets) =>
            (widgets ?? [])
                .Where(widget => widget != null && !string.IsNullOrWhiteSpace(widget.Id))
                .GroupBy(widget => widget.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        private static bool WidgetsEquivalent(WebDavSyncWidget left, WebDavSyncWidget right)
        {
            if (!string.Equals(left.Html, right.Html, StringComparison.Ordinal) ||
                left.State.Count != right.State.Count)
            {
                return false;
            }
            foreach (KeyValuePair<string, JsonElement> item in left.State)
            {
                if (!right.State.TryGetValue(item.Key, out JsonElement other) ||
                    !string.Equals(item.Value.GetRawText(), other.GetRawText(), StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static HtmlWidgetDefinition ApplyPortable(
            HtmlWidgetDefinition local,
            WebDavSyncWidget remote)
        {
            HtmlWidgetDefinition result = CloneDefinition(local);
            result.Html = remote.Html;
            result.State = CloneState(remote.State);
            return result;
        }

        private static HtmlWidgetDefinition CreateImported(
            WebDavSyncWidget remote,
            bool conflictCopy)
        {
            string html = remote.Html;
            if (conflictCopy)
            {
                string title = HtmlWidgetTitle.GetHtmlTitle(html);
                if (string.IsNullOrWhiteSpace(title))
                    title = "未命名组件";
                html = HtmlWidgetTitle.SetHtmlTitle(html, title + "（来自其他设备）");
            }
            var result = new HtmlWidgetDefinition
            {
                Id = conflictCopy ? Guid.NewGuid().ToString("N") : remote.Id,
                Html = html,
                Home = HtmlWidgetHome.Library,
                State = CloneState(remote.State)
            };
            HtmlWidgetCanvasStore.Normalize(result);
            return result;
        }

        private static HtmlWidgetDefinition CloneDefinition(HtmlWidgetDefinition source) => new()
        {
            Id = source.Id,
            Html = source.Html,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height,
            IsLocked = source.IsLocked,
            CanvasId = source.CanvasId,
            Home = source.Home,
            DetachedPosition = source.DetachedPosition,
            DetachedWidth = source.DetachedWidth,
            DetachedHeight = source.DetachedHeight,
            DetachedTopmost = source.DetachedTopmost,
            DetachedAutoHide = source.DetachedAutoHide,
            State = CloneState(source.State)
        };

        private static Dictionary<string, JsonElement> CloneState(
            IReadOnlyDictionary<string, JsonElement>? state)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (state == null)
                return result;
            foreach (KeyValuePair<string, JsonElement> item in state)
                result[item.Key] = item.Value.Clone();
            return result;
        }

        private static void EnsureUniqueTitles(IReadOnlyList<HtmlWidgetDefinition> widgets)
        {
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HtmlWidgetDefinition widget in widgets)
            {
                string title = HtmlWidgetTitle.GetDisplayName(widget);
                string unique = title;
                for (int suffix = 2; !titles.Add(unique); suffix++)
                    unique = title + " " + suffix;
                if (!string.Equals(unique, title, StringComparison.Ordinal))
                    widget.Html = HtmlWidgetTitle.SetHtmlTitle(widget.Html, unique);
            }
        }
    }
}
