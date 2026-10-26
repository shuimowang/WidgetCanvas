using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WidgetCanvas.HtmlWidgets;
using WidgetCanvas.Services;

namespace WidgetCanvas.Tests;

public sealed class WebDavSyncTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "WidgetCanvas.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConnectionOptionsUseAManagedDirectoryAndFixedRemoteFile()
    {
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/user/widgets",
            " alice ",
            "secret");

        Assert.Equal("https://dav.example.test/user/widgets/", options.ParentDirectoryUri.AbsoluteUri);
        Assert.Equal("https://dav.example.test/user/widgets/WidgetCanvas/", options.DirectoryUri.AbsoluteUri);
        Assert.Equal(
            "https://dav.example.test/user/widgets/WidgetCanvas/widgetcanvas-sync-v1.json",
            options.FileUri.AbsoluteUri);
        Assert.Equal("alice", options.Username);
        WebDavConnectionOptions alreadyManaged = WebDavSyncService.CreateOptions(
            "https://dav.example.test/user/widgets/WidgetCanvas/",
            "",
            "");
        Assert.Equal(
            "https://dav.example.test/user/widgets/WidgetCanvas/",
            alreadyManaged.DirectoryUri.AbsoluteUri);
        Assert.Throws<ArgumentException>(() =>
            WebDavSyncService.CreateOptions("ftp://example.test/data", "", ""));
    }

    [Fact]
    public async Task TestConnectionCreatesManagedDirectoryWhenItDoesNotExist()
    {
        var handler = new DirectoryCreatingWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/dav/",
            "user",
            "password");

        await service.TestConnectionAsync(options);

        Assert.True(handler.DirectoryCreated);
        Assert.Equal(
            "https://dav.example.test/dav/WidgetCanvas",
            handler.CreatedDirectoryUri?.AbsoluteUri);
    }

    [Fact]
    public void PasswordRoundTripsWithCurrentWindowsUser()
    {
        const string password = "p@ss-同步-123";
        string protectedValue = SecretProtector.Protect(password);

        Assert.NotEqual(password, protectedValue);
        Assert.Equal(password, SecretProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void RemoteChangePreservesLocalLayoutAndDeviceOptions()
    {
        HtmlWidgetDefinition local = CreateWidget("a", "便签", "old", x: 120, y: 80);
        local.DetachedTopmost = false;
        local.DetachedAutoHide = true;
        WebDavSyncDocument baseline = WebDavSyncService.CreateDocument([local], "device-a");
        HtmlWidgetDefinition remoteVersion = CreateWidget("a", "新便签", "remote", x: 1, y: 2);
        WebDavSyncDocument remote = WebDavSyncService.CreateDocument([remoteVersion], "device-b");

        WebDavMergeResult merge = WebDavSyncMerger.Merge([local], remote, baseline);

        HtmlWidgetDefinition merged = Assert.Single(merge.Widgets);
        Assert.True(merge.LocalDataChanged);
        Assert.Equal("新便签", HtmlWidgetTitle.GetDisplayName(merged));
        Assert.Equal("remote", merged.State["text"].GetString());
        Assert.Equal(120, merged.X);
        Assert.Equal(80, merged.Y);
        Assert.False(merged.DetachedTopmost);
        Assert.True(merged.DetachedAutoHide);
    }

    [Fact]
    public void ConcurrentEditsKeepBothVersionsAndRemoteNewWidgetsEnterLibrary()
    {
        HtmlWidgetDefinition original = CreateWidget("a", "便签", "base", x: 40, y: 50);
        HtmlWidgetDefinition deletedRemotely = CreateWidget("b", "待办", "base", x: 10, y: 10);
        WebDavSyncDocument baseline = WebDavSyncService.CreateDocument(
            [original, deletedRemotely],
            "device-a");

        HtmlWidgetDefinition localEdit = CreateWidget("a", "便签", "local", x: 40, y: 50);
        HtmlWidgetDefinition remoteEdit = CreateWidget("a", "便签", "remote", x: 900, y: 900);
        HtmlWidgetDefinition remoteNew = CreateWidget("c", "便签", "new", x: 700, y: 700);
        WebDavSyncDocument remote = WebDavSyncService.CreateDocument(
            [remoteEdit, remoteNew],
            "device-b");

        WebDavMergeResult merge = WebDavSyncMerger.Merge(
            [localEdit, deletedRemotely],
            remote,
            baseline);

        Assert.Equal(1, merge.ConflictCount);
        Assert.Equal(3, merge.Widgets.Count);
        Assert.DoesNotContain(merge.Widgets, widget => widget.Id == "b");
        Assert.Contains(merge.Widgets, widget =>
            widget.Id == "a" && widget.State["text"].GetString() == "local");
        Assert.All(
            merge.Widgets.Where(widget => widget.Id != "a"),
            widget => Assert.Equal(HtmlWidgetHome.Library, widget.Home));
        string[] titles = merge.Widgets.Select(HtmlWidgetTitle.GetDisplayName).ToArray();
        Assert.Equal(titles.Length, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(titles, title => title.Contains("来自其他设备", StringComparison.Ordinal));
    }

    [Fact]
    public void LayoutOnlyChangesAreNotUploaded()
    {
        HtmlWidgetDefinition baselineWidget = CreateWidget("a", "便签", "same", x: 10, y: 20);
        WebDavSyncDocument baseline = WebDavSyncService.CreateDocument([baselineWidget], "device-a");
        HtmlWidgetDefinition moved = CreateWidget("a", "便签", "same", x: 500, y: 600);

        WebDavMergeResult merge = WebDavSyncMerger.Merge([moved], baseline, baseline);

        Assert.False(merge.LocalDataChanged);
        HtmlWidgetDefinition result = Assert.Single(merge.Widgets);
        Assert.Equal(500, result.X);
        Assert.Equal(600, result.Y);
    }

    [Fact]
    public async Task ServiceCreatesRemoteFileAndPersistsSyncBaseline()
    {
        string contentPath = Path.Combine(_directory, "components", "widgets.json");
        string runtimePath = Path.Combine(_directory, "state", "canvas.json");
        string basePath = Path.Combine(_directory, "sync", "base.json");
        HtmlWidgetCanvasStore.Save(
            contentPath,
            runtimePath,
            [CreateWidget("a", "便签", "hello", x: 10, y: 20)]);
        var handler = new MemoryWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/widgets/",
            "user",
            "password");

        await service.TestConnectionAsync(options);
        WebDavSyncResult first = await service.SynchronizeAsync(
            options,
            "device-a",
            contentPath,
            runtimePath,
            basePath);
        WebDavSyncResult second = await service.SynchronizeAsync(
            options,
            "device-a",
            contentPath,
            runtimePath,
            basePath);

        Assert.True(first.RemoteDataChanged);
        Assert.False(second.RemoteDataChanged);
        Assert.NotNull(handler.RemoteJson);
        Assert.True(File.Exists(basePath));
        Assert.Contains("数据已是最新", second.Message, StringComparison.Ordinal);
        Assert.Equal("Basic", handler.LastAuthorizationScheme);
    }

    [Theory]
    [InlineData(false, "corrupt")]
    [InlineData(true, "corrupt")]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "locked")]
    [InlineData(true, "locked")]
    public async Task DamagedLocalDataDoesNotReachOrOverwriteRemote(bool damageRuntime, string damage)
    {
        string contentPath = Path.Combine(_directory, "components", "widgets.json");
        string runtimePath = Path.Combine(_directory, "state", "canvas.json");
        string basePath = Path.Combine(_directory, "sync", "base.json");
        HtmlWidgetCanvasStore.Save(
            contentPath,
            runtimePath,
            [CreateWidget("a", "便签", "保留的数据", x: 10, y: 20)]);
        var handler = new MemoryWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/widgets/", "user", "password");
        await service.SynchronizeAsync(options, "device-a", contentPath, runtimePath, basePath);
        byte[] remoteBefore = handler.RemoteJson!.ToArray();
        byte[] baselineBefore = File.ReadAllBytes(basePath);
        int requestsBefore = handler.RequestCount;
        string damagedPath = damageRuntime ? runtimePath : contentPath;
        if (damage == "missing")
            File.Delete(damagedPath);
        else if (damage == "corrupt")
            File.WriteAllText(damagedPath, "not json");
        using FileStream? fileLock = damage == "locked"
            ? new FileStream(damagedPath, FileMode.Open, FileAccess.Read, FileShare.None)
            : null;

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SynchronizeAsync(
            options, "device-a", contentPath, runtimePath, basePath));

        Assert.Equal(requestsBefore, handler.RequestCount);
        Assert.Equal(remoteBefore, handler.RemoteJson);
        Assert.Equal(baselineBefore, File.ReadAllBytes(basePath));
        if (damage == "corrupt")
            Assert.Equal("not json", File.ReadAllText(damagedPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackupRecoveryDoesNotUploadAnOlderSnapshot(bool damageRuntime)
    {
        string contentPath = Path.Combine(_directory, "components", "widgets.json");
        string runtimePath = Path.Combine(_directory, "state", "canvas.json");
        string basePath = Path.Combine(_directory, "sync", "base.json");
        HtmlWidgetCanvasStore.Save(
            contentPath, runtimePath, [CreateWidget("a", "便签", "older", x: 10, y: 20)]);
        HtmlWidgetCanvasStore.Save(
            contentPath, runtimePath, [CreateWidget("a", "最新便签", "latest", x: 10, y: 20)]);
        var handler = new MemoryWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/widgets/", "user", "password");
        await service.SynchronizeAsync(options, "device-a", contentPath, runtimePath, basePath);
        byte[] remoteBefore = handler.RemoteJson!.ToArray();
        byte[] baselineBefore = File.ReadAllBytes(basePath);
        int requestsBefore = handler.RequestCount;
        File.WriteAllText(damageRuntime ? runtimePath : contentPath, "not json");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SynchronizeAsync(
            options, "device-a", contentPath, runtimePath, basePath));

        Assert.Equal(requestsBefore, handler.RequestCount);
        Assert.Equal(remoteBefore, handler.RemoteJson);
        Assert.Equal(baselineBefore, File.ReadAllBytes(basePath));
    }

    [Fact]
    public async Task ValidEmptyLibraryStillUploadsIntentionalDeletions()
    {
        string contentPath = Path.Combine(_directory, "components", "widgets.json");
        string runtimePath = Path.Combine(_directory, "state", "canvas.json");
        string basePath = Path.Combine(_directory, "sync", "base.json");
        HtmlWidgetCanvasStore.Save(
            contentPath,
            runtimePath,
            [CreateWidget("a", "便签", "hello", x: 10, y: 20)]);
        var handler = new MemoryWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/widgets/", "user", "password");
        await service.SynchronizeAsync(options, "device-a", contentPath, runtimePath, basePath);
        HtmlWidgetCanvasStore.Save(contentPath, runtimePath, []);

        WebDavSyncResult result = await service.SynchronizeAsync(
            options, "device-a", contentPath, runtimePath, basePath);

        Assert.True(result.RemoteDataChanged);
        Assert.Equal(0, result.WidgetCount);
        using JsonDocument remote = JsonDocument.Parse(handler.RemoteJson!);
        Assert.Empty(remote.RootElement.GetProperty("widgets").EnumerateArray());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task LocalEditsDuringNetworkRequestsAreNotOverwrittenOrMarkedSynchronized(string method)
    {
        string contentPath = Path.Combine(_directory, "components", "widgets.json");
        string runtimePath = Path.Combine(_directory, "state", "canvas.json");
        string basePath = Path.Combine(_directory, "sync", "base.json");
        HtmlWidgetCanvasStore.Save(
            contentPath, runtimePath, [CreateWidget("a", "便签", "baseline", x: 10, y: 20)]);
        var handler = new MemoryWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/widgets/", "user", "password");
        await service.SynchronizeAsync(options, "device-a", contentPath, runtimePath, basePath);
        byte[] baselineBefore = File.ReadAllBytes(basePath);
        handler.RemoteJson = JsonSerializer.SerializeToUtf8Bytes(WebDavSyncService.CreateDocument(
            [CreateWidget("a", "远端便签", "remote edit", x: 10, y: 20)], "device-b"));
        byte[] remoteBefore = handler.RemoteJson.ToArray();
        HtmlWidgetCanvasStore.Save(
            contentPath, runtimePath, [CreateWidget("a", "本机便签", "before sync", x: 10, y: 20)]);
        handler.PauseOnMethod = new HttpMethod(method);
        Task<WebDavSyncResult> pending = service.SynchronizeAsync(
            options, "device-a", contentPath, runtimePath, basePath);

        await handler.RequestPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            HtmlWidgetCanvasStore.Save(
                contentPath, runtimePath, [CreateWidget("a", "最新本机便签", "edited during sync", x: 70, y: 80)]);
        }
        finally
        {
            handler.ContinueRequest.TrySetResult();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);

        HtmlWidgetDefinition local = Assert.Single(HtmlWidgetCanvasStore.Load(contentPath, runtimePath).Widgets);
        Assert.Equal("最新本机便签", HtmlWidgetTitle.GetDisplayName(local));
        Assert.Equal("edited during sync", local.State["text"].GetString());
        Assert.Equal(70, local.X);
        Assert.Equal(baselineBefore, File.ReadAllBytes(basePath));
        if (method == "GET")
            Assert.Equal(remoteBefore, handler.RemoteJson);
        else
            Assert.NotEqual(remoteBefore, handler.RemoteJson);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task PendingMemoryEditsStopSynchronizationBeforeApplyingAStaleSnapshot(string phase)
    {
        string contentPath = Path.Combine(_directory, "components", "widgets.json");
        string runtimePath = Path.Combine(_directory, "state", "canvas.json");
        string basePath = Path.Combine(_directory, "sync", "base.json");
        HtmlWidgetCanvasStore.Save(
            contentPath, runtimePath, [CreateWidget("a", "便签", "baseline", x: 10, y: 20)]);
        var handler = new MemoryWebDavHandler();
        using var service = new WebDavSyncService(handler);
        WebDavConnectionOptions options = WebDavSyncService.CreateOptions(
            "https://dav.example.test/widgets/", "user", "password");
        await service.SynchronizeAsync(options, "device-a", contentPath, runtimePath, basePath);
        handler.RemoteJson = JsonSerializer.SerializeToUtf8Bytes(WebDavSyncService.CreateDocument(
            [CreateWidget("a", "远端便签", "remote edit", x: 10, y: 20)], "device-b"));
        HtmlWidgetCanvasStore.Save(
            contentPath, runtimePath, [CreateWidget("a", "本机便签", "before sync", x: 10, y: 20)]);
        byte[] contentBefore = File.ReadAllBytes(contentPath);
        byte[] runtimeBefore = File.ReadAllBytes(runtimePath);
        byte[] baselineBefore = File.ReadAllBytes(basePath);
        byte[] remoteBefore = handler.RemoteJson.ToArray();
        int requestsBefore = handler.RequestCount;
        int putsBefore = handler.PutCount;
        bool pendingMemoryEdits = phase == "start";
        if (phase != "start")
            handler.PauseOnMethod = new HttpMethod(phase);

        Task<WebDavSyncResult> pending = service.SynchronizeAsync(
            options, "device-a", contentPath, runtimePath, basePath,
            verifyLocalState: () =>
            {
                Assert.False(Monitor.IsEntered(HtmlWidgetCanvasStore.SyncRoot));
                if (pendingMemoryEdits)
                    throw new InvalidOperationException("unsaved local edits");
            });
        if (phase != "start")
        {
            await handler.RequestPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pendingMemoryEdits = true;
            handler.ContinueRequest.TrySetResult();
        }
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);

        Assert.Equal("unsaved local edits", error.Message);
        Assert.Equal(contentBefore, File.ReadAllBytes(contentPath));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(runtimePath));
        Assert.Equal(baselineBefore, File.ReadAllBytes(basePath));
        Assert.Equal(putsBefore + (phase == "PUT" ? 1 : 0), handler.PutCount);
        if (phase == "start")
            Assert.Equal(requestsBefore, handler.RequestCount);
        if (phase != "PUT")
            Assert.Equal(remoteBefore, handler.RemoteJson);
    }

    private static HtmlWidgetDefinition CreateWidget(
        string id,
        string title,
        string text,
        double x,
        double y) => new()
    {
        Id = id,
        Html = $"<!doctype html><html><head><title>{title}</title><style></style></head><body></body></html>",
        X = x,
        Y = y,
        Width = 320,
        Height = 220,
        State = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["text"] = JsonSerializer.SerializeToElement(text)
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class MemoryWebDavHandler : HttpMessageHandler
    {
        public byte[]? RemoteJson { get; set; }

        public int RequestCount { get; private set; }

        public int PutCount { get; private set; }

        public HttpMethod? PauseOnMethod { get; set; }

        public TaskCompletionSource RequestPaused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ContinueRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? LastAuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            if (request.Method == PauseOnMethod)
            {
                PauseOnMethod = null;
                RequestPaused.TrySetResult();
                await ContinueRequest.Task.WaitAsync(cancellationToken);
            }
            if (request.Method.Method == "PROPFIND")
                return new HttpResponseMessage((HttpStatusCode)207);
            if (request.Method == HttpMethod.Get)
            {
                if (RemoteJson == null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(RemoteJson),
                    Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"") }
                };
            }
            if (request.Method == HttpMethod.Put)
            {
                PutCount++;
                RemoteJson = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"") }
                };
            }
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }

    private sealed class DirectoryCreatingWebDavHandler : HttpMessageHandler
    {
        public bool DirectoryCreated { get; private set; }

        public Uri? CreatedDirectoryUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method.Method == "PROPFIND")
            {
                bool isManagedDirectory = path.TrimEnd('/').EndsWith(
                    "/WidgetCanvas",
                    StringComparison.Ordinal);
                HttpStatusCode status = isManagedDirectory && !DirectoryCreated
                    ? HttpStatusCode.NotFound
                    : (HttpStatusCode)207;
                return Task.FromResult(new HttpResponseMessage(status));
            }
            if (request.Method.Method == "MKCOL")
            {
                DirectoryCreated = true;
                CreatedDirectoryUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        }
    }
}
