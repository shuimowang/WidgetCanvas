using System.Windows.Input;
using System.Text.Json;
using WidgetCanvas.Services;

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
        service.Save();

        service.Settings.Hotkey = "Alt+F2";
        service.Save();
        File.WriteAllText(path, "not-json");

        var recovered = new AppSettingsService(path);
        Assert.False(recovered.Settings.AutoUpdateEnabled);
        Assert.True(recovered.Settings.HotkeyEnabled);
        Assert.Equal("Ctrl+Shift+F9", recovered.Settings.Hotkey);
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
    public void UpdateReleaseParserFindsExecutableAndChecksumAssets()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "tag_name": "v1.4.2",
              "html_url": "https://github.com/shuimowang/WidgetCanvas/releases/tag/v1.4.2",
              "assets": [
                { "name": "WidgetCanvas-win-x64.zip", "browser_download_url": "https://example.invalid/app.zip" },
                { "name": "WidgetCanvas-win-x64.exe", "browser_download_url": "https://example.invalid/app.exe" },
                { "name": "WidgetCanvas-win-x64.exe.sha256", "browser_download_url": "https://example.invalid/app.sha256" }
              ]
            }
            """);

        UpdateCheckResult result = UpdateService.ParseRelease(document.RootElement, new Version(1, 3, 0, 0));

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(1, 4, 2), result.LatestVersion);
        Assert.Equal("https://example.invalid/app.exe", result.DownloadUrl);
        Assert.Equal("https://example.invalid/app.sha256", result.ChecksumUrl);
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
