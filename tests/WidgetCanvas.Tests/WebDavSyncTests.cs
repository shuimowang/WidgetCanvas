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
        public byte[]? RemoteJson { get; private set; }

        public string? LastAuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
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
