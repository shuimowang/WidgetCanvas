using System.Text.RegularExpressions;
using WidgetCanvas.HtmlWidgets;

namespace WidgetCanvas.Tests;

public sealed class HtmlWidgetCanvasPromptTests
{
    private static readonly string[] HostMethods =
    [
        "state.read",
        "state.write",
        "state.remove",
        "state.clear",
        "state.flush",
        "clipboard.read",
        "clipboard.write",
        "url.open",
        "path.open",
        "window.hide",
        "http.get",
        "http.post",
        "http.request",
        "fs.getKnownFolders",
        "fs.exists",
        "fs.getInfo",
        "fs.readText",
        "fs.readBase64",
        "fs.list",
        "fs.selectFile",
        "fs.selectFolder",
        "process.start",
        "process.run"
    ];

    [Fact]
    public void CreatePromptDocumentsEveryHostMethod()
    {
        string prompt = HtmlWidgetCanvasPrompt.BuildCreate(360, 240, "制作测试组件");

        foreach (string method in HostMethods)
            Assert.Contains("host." + method, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("KkjQuicker", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("宽 360 DIP，高 240 DIP", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePromptOnlyDocumentsKnownHostMethods()
    {
        string prompt = HtmlWidgetCanvasPrompt.BuildCreateTemplate();
        string[] documented = Regex.Matches(prompt, @"host\.([a-z]+\.[A-Za-z0-9]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(documented.Except(HostMethods, StringComparer.Ordinal));
    }
}
