using System.Text.Json;
using WidgetCanvas.HtmlWidgets;

namespace WidgetCanvas.Tests;

public sealed class HtmlWidgetCanvasStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "WidgetCanvas.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveSeparatesSourceFromRuntimeStateAndRoundTrips()
    {
        string contentPath = Path.Combine(_directory, "Documents", "widgets.json");
        string runtimePath = Path.Combine(_directory, "LocalAppData", "canvas.json");
        using JsonDocument stateDocument = JsonDocument.Parse("{\"text\":\"hello\"}");
        var widget = new HtmlWidgetDefinition
        {
            Id = "note",
            Html = "<!doctype html><title>便签</title>",
            X = 42,
            Y = 24,
            Width = 360,
            Height = 220,
            DetachedTopmost = false,
            DetachedAutoHide = true,
            State = new Dictionary<string, JsonElement>
            {
                ["note"] = stateDocument.RootElement.Clone()
            }
        };

        HtmlWidgetCanvasStore.Save(contentPath, runtimePath, [widget]);

        string contentJson = File.ReadAllText(contentPath);
        string runtimeJson = File.ReadAllText(runtimePath);
        using JsonDocument contentDocument = JsonDocument.Parse(contentJson);
        Assert.Equal(
            widget.Html,
            contentDocument.RootElement.GetProperty("Widgets")[0].GetProperty("Html").GetString());
        Assert.DoesNotContain("DetachedTopmost", contentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("State", contentJson, StringComparison.Ordinal);
        Assert.Contains("DetachedTopmost", runtimeJson, StringComparison.Ordinal);
        Assert.Contains("hello", runtimeJson, StringComparison.Ordinal);

        HtmlWidgetCanvasLoadResult result = HtmlWidgetCanvasStore.Load(contentPath, runtimePath);
        HtmlWidgetDefinition loaded = Assert.Single(result.Widgets);
        Assert.Equal(widget.Id, loaded.Id);
        Assert.Equal(widget.Html, loaded.Html);
        Assert.Equal(42, loaded.X);
        Assert.False(loaded.DetachedTopmost);
        Assert.True(loaded.DetachedAutoHide);
        Assert.Equal("hello", loaded.State["note"].GetProperty("text").GetString());
    }

    [Fact]
    public void LoadRecoversLastContentBackup()
    {
        string contentPath = Path.Combine(_directory, "widgets.json");
        string runtimePath = Path.Combine(_directory, "canvas.json");
        var widget = new HtmlWidgetDefinition
        {
            Id = "note",
            Html = "<!doctype html><title>第一版</title>"
        };
        HtmlWidgetCanvasStore.Save(contentPath, runtimePath, [widget]);
        widget.Html = "<!doctype html><title>第二版</title>";
        HtmlWidgetCanvasStore.Save(contentPath, runtimePath, [widget]);
        File.WriteAllText(contentPath, "not json");

        HtmlWidgetCanvasLoadResult result = HtmlWidgetCanvasStore.Load(contentPath, runtimePath);

        Assert.True(result.RecoveredFromBackup);
        Assert.Contains("组件数据已从上一次备份恢复", result.Notice, StringComparison.Ordinal);
        Assert.Equal("<!doctype html><title>第一版</title>", Assert.Single(result.Widgets).Html);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
