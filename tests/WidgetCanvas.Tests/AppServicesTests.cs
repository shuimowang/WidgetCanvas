using System.Windows.Input;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using WidgetCanvas.HtmlWidgets;
using WidgetCanvas.Services;
using WidgetCanvas.Windows;

namespace WidgetCanvas.Tests;

public sealed class AppServicesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "WidgetCanvas.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SettingsRoundTripAndRecoverFromBackup()
    {
        string path = Path.Combine(_directory, "settings.json");
        var service = new AppSettingsService(path);
        service.Settings.AutoUpdateEnabled = false;
        service.Settings.HotkeyEnabled = true;
        service.Settings.Hotkey = "Ctrl+Shift+F9";
        service.Settings.WebDavAutoSyncEnabled = true;
        service.Settings.WebDavUrl = "https://dav.example.test/widgets/";
        service.Settings.WebDavUsername = "alice";
        service.Settings.WebDavProtectedPassword = "encrypted";
        service.Settings.WebDavLastError = "timeout";
        service.Save();

        service.Settings.Hotkey = "Alt+F2";
        service.Save();
        File.WriteAllText(path, "not-json");

        var recovered = new AppSettingsService(path);
        Assert.False(recovered.Settings.AutoUpdateEnabled);
        Assert.True(recovered.Settings.HotkeyEnabled);
        Assert.Equal("Ctrl+Shift+F9", recovered.Settings.Hotkey);
        Assert.True(recovered.Settings.WebDavAutoSyncEnabled);
        Assert.Equal("https://dav.example.test/widgets/", recovered.Settings.WebDavUrl);
        Assert.Equal("alice", recovered.Settings.WebDavUsername);
        Assert.Equal("encrypted", recovered.Settings.WebDavProtectedPassword);
        Assert.Equal("timeout", recovered.Settings.WebDavLastError);
    }

    [Theory]
    [InlineData("Ctrl+Alt+W", ModifierKeys.Control | ModifierKeys.Alt, Key.W)]
    [InlineData("Shift+Win+F9", ModifierKeys.Windows | ModifierKeys.Shift, Key.F9)]
    public void HotkeyGestureParsesCanonicalText(string text, ModifierKeys modifiers, Key key)
    {
        HotkeyGesture gesture = HotkeyGesture.Parse(text);

        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
        Assert.Equal(text, HotkeyGesture.FromInput(key, modifiers));
    }

    [Theory]
    [InlineData("W")]
    [InlineData("Ctrl+UnknownKey")]
    public void HotkeyGestureRejectsUnsafeOrInvalidText(string text)
    {
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse(text));
    }

    [Fact]
    public void UpdateReleaseParserBuildsStableAssetUrlsFromLatestRedirect()
    {
        UpdateCheckResult result = UpdateService.ParseLatestReleaseUri(
            new Uri("https://github.com/shuimowang/WidgetCanvas/releases/tag/v1.4.2"),
            new Version(1, 3, 0, 0));

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 4, 2), result.LatestVersion);
        Assert.Equal(
            "https://github.com/shuimowang/WidgetCanvas/releases/download/v1.4.2/WidgetCanvas-win-x64.exe",
            result.DownloadUrl);
        Assert.Equal(
            "https://github.com/shuimowang/WidgetCanvas/releases/download/v1.4.2/WidgetCanvas-win-x64.exe.sha256",
            result.ChecksumUrl);
    }

    [Theory]
    [InlineData("https://github.com/shuimowang/WidgetCanvas/releases")]
    [InlineData("https://example.com/shuimowang/WidgetCanvas/releases/tag/v1.4.2")]
    [InlineData("https://github.com/shuimowang/WidgetCanvas/releases/tag/latest")]
    public void UpdateReleaseParserRejectsUnexpectedRedirects(string url)
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ParseLatestReleaseUri(new Uri(url), new Version(1, 3, 0)));
    }

    [Theory]
    [InlineData("--exit")]
    [InlineData("--EXIT")]
    public void ExitArgumentIsCaseInsensitive(string argument)
    {
        Assert.True(App.IsExitRequest([argument]));
        Assert.False(App.IsExitRequest(["--background"]));
    }

    [Fact]
    public void FileWidgetArgumentIsNormalizedBeforeForwarding()
    {
        string relativePath = Path.Combine("widgets", "clock.html");

        string[] normalized = App.NormalizeFileArgument(["--FILE", relativePath]);

        Assert.Equal("--FILE", normalized[0]);
        Assert.Equal(Path.GetFullPath(relativePath), normalized[1]);
    }

    [Fact]
    public void FileWidgetReaderAcceptsCompleteHtmlAndRejectsIncompleteContent()
    {
        Directory.CreateDirectory(_directory);
        string validPath = Path.Combine(_directory, "clock.html");
        string invalidPath = Path.Combine(_directory, "broken.html");
        const string html = "<!doctype html><html><head><title>时钟</title><style>body{margin:0}</style></head><body>12:00</body></html>";
        File.WriteAllText(validPath, html);
        File.WriteAllText(invalidPath, "<html><body>missing title and style</body></html>");

        Assert.Equal(html, HtmlWidgetCanvasWindow.ReadFileWidgetHtml(validPath));
        Assert.Throws<InvalidDataException>(() => HtmlWidgetCanvasWindow.ReadFileWidgetHtml(invalidPath));
    }

    [Fact]
    public void FileWidgetStatePersistsBySourcePathWithoutCopyingHtml()
    {
        string sourcePath = Path.Combine(_directory, "clock.html");
        string stateFolder = Path.Combine(_directory, "file-state");
        const string firstHtml = "<!doctype html><html><head><title>初版</title><style></style></head><body></body></html>";
        const string updatedHtml = "<!doctype html><html><head><title>新版</title><style></style></head><body></body></html>";
        HtmlWidgetDefinition widget = HtmlFileWidgetStateStore.Load(sourcePath, firstHtml, stateFolder);
        widget.DetachedPosition = "10,20,360,280";
        widget.DetachedWidth = 360;
        widget.DetachedHeight = 243;
        using JsonDocument value = JsonDocument.Parse("{\"theme\":\"dark\"}");
        widget.State["settings"] = value.RootElement.Clone();
        HtmlFileWidgetStateStore.Save(widget, stateFolder);

        HtmlWidgetDefinition restored = HtmlFileWidgetStateStore.Load(sourcePath, updatedHtml, stateFolder);

        Assert.Equal(updatedHtml, restored.Html);
        Assert.Equal("10,20,360,280", restored.DetachedPosition);
        Assert.Equal(360, restored.DetachedWidth);
        Assert.Equal("dark", restored.State["settings"].GetProperty("theme").GetString());
        Assert.True(restored.IsFileBacked);
        Assert.Equal(Path.GetFullPath(sourcePath), restored.SourceFilePath);
    }

    [Fact]
    public void UpdateInstallerReplacesTargetAtomically()
    {
        Directory.CreateDirectory(_directory);
        string source = Path.Combine(_directory, "staged.exe");
        string target = Path.Combine(_directory, "WidgetCanvas.exe");
        File.WriteAllText(source, "new-version");
        File.WriteAllText(target, "old-version");

        UpdateInstaller.ReplaceExecutable(source, target);

        Assert.Equal("new-version", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".new"));
    }

    [Fact]
    public void WidgetCatalogPublishesSnapshotAndSignalsOnlyOnMeaningfulChanges()
    {
        string snapshotPath = Path.Combine(_directory, "integration", "widgets.json");
        string eventName = @"Local\WidgetCanvas.Tests." + Guid.NewGuid().ToString("N");
        using var integration = new WidgetCatalogIntegration(snapshotPath, eventName);
        using EventWaitHandle changed = EventWaitHandle.OpenExisting(eventName);
        WidgetCatalogEntry[] initial =
        [
            new("b", "天气", HtmlWidgetHome.Library),
            new("a", "便签", HtmlWidgetHome.Canvas)
        ];

        Assert.True(integration.PublishIfChanged(initial));
        Assert.True(changed.WaitOne(0));
        var snapshotOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        snapshotOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        WidgetCatalogSnapshot first = JsonSerializer.Deserialize<WidgetCatalogSnapshot>(
            File.ReadAllText(snapshotPath),
            snapshotOptions)!;
        Assert.Equal(1, first.Revision);
        Assert.Equal(["便签", "天气"], first.Titles);

        Assert.False(integration.PublishIfChanged(initial.Reverse()));
        Assert.False(changed.WaitOne(0));

        WidgetCatalogEntry[] renamed =
        [
            initial[0],
            initial[1] with { Title = "护眼便签" }
        ];
        Assert.True(integration.PublishIfChanged(renamed));
        Assert.True(changed.WaitOne(0));
        WidgetCatalogSnapshot second = JsonSerializer.Deserialize<WidgetCatalogSnapshot>(
            File.ReadAllText(snapshotPath),
            snapshotOptions)!;
        Assert.Equal(2, second.Revision);
        Assert.Contains("护眼便签", second.Titles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
