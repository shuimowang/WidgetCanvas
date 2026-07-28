using System.Text.Json;
using WidgetCanvas.HtmlWidgets;

namespace WidgetCanvas.Tests;

public sealed class HtmlWidgetHostTests
{
    [Fact]
    public void BridgeIsOnlyInstalledInTheTopLevelDocument()
    {
        Assert.Contains(
            "if (window.top !== window) return;",
            HtmlWidgetHostBridge.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StateSetClonesValuesAndAllowsReplacement()
    {
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using (JsonDocument document = JsonDocument.Parse("""{"items":[1,2,3]}"""))
            HtmlWidgetState.Set(state, "todo", document.RootElement);

        using (JsonDocument replacement = JsonDocument.Parse("true"))
            HtmlWidgetState.Set(state, "todo", replacement.RootElement);

        Assert.Single(state);
        Assert.True(state["todo"].GetBoolean());
    }

    [Fact]
    public void StateSetRejectsAnOversizedKeyWithoutChangingState()
    {
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse("1");

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            HtmlWidgetState.Set(
                state,
                new string('k', HtmlWidgetState.MaximumKeyLength + 1),
                document.RootElement));

        Assert.Contains("状态键", error.Message, StringComparison.Ordinal);
        Assert.Empty(state);
    }

    [Fact]
    public void StateSetAllowsReplacementButRejectsANewEntryAtTheLimit()
    {
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse("1");
        for (int index = 0; index < HtmlWidgetState.MaximumEntryCount; index++)
            state[index.ToString()] = document.RootElement.Clone();

        HtmlWidgetState.Set(state, "0", document.RootElement);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            HtmlWidgetState.Set(state, "extra", document.RootElement));

        Assert.Contains(
            HtmlWidgetState.MaximumEntryCount.ToString(),
            error.Message,
            StringComparison.Ordinal);
        Assert.Equal(HtmlWidgetState.MaximumEntryCount, state.Count);
    }

    [Fact]
    public void StateSetRejectsOversizedDataWithoutChangingState()
    {
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        string json = JsonSerializer.Serialize(new string(
            'x',
            HtmlWidgetState.MaximumUtf8Bytes));
        using JsonDocument document = JsonDocument.Parse(json);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            HtmlWidgetState.Set(state, "large", document.RootElement));

        Assert.Contains("4 MiB", error.Message, StringComparison.Ordinal);
        Assert.Empty(state);
    }
}
