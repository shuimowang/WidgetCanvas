using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using WidgetCanvas.Services;
using WidgetCanvas.Windows;

namespace WidgetCanvas.Tests;

[CollectionDefinition("WidgetProcess", DisableParallelization = true)]
public sealed class WidgetProcessCollection;

[Collection("WidgetProcess")]
public sealed class WidgetProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "WidgetCanvas.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProcessRunCapturesInputAndBothOutputStreams()
    {
        JsonElement result = await RunAsync(new
        {
            file = PowerShellPath,
            args = new[] { "-NoProfile", "-NonInteractive", "-Command",
                "$inputText = [Console]::In.ReadToEnd(); [Console]::Out.Write($inputText); [Console]::Error.Write('error-output')" },
            input = "组件输入",
            timeoutMs = 10000
        });

        Assert.False(result.GetProperty("timedOut").GetBoolean());
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("组件输入", result.GetProperty("stdout").GetString());
        Assert.Equal("error-output", result.GetProperty("stderr").GetString());
    }

    [Fact]
    public async Task ProcessRunTimeoutStopsTheStartedProcess()
    {
        JsonElement result = await RunAsync(new
        {
            file = PowerShellPath,
            args = new[] { "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::Out.WriteLine($PID); Start-Sleep -Seconds 30" },
            timeoutMs = 1500
        });

        Assert.True(result.GetProperty("timedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("exitCode").ValueKind);
        int processId = int.Parse(result.GetProperty("stdout").GetString()!.Trim());
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
    }

    [Fact]
    public async Task ProcessRunTimeoutAlsoClosesBlockedStandardInput()
    {
        JsonElement result = await RunAsync(new
        {
            file = PowerShellPath,
            args = new[] { "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::Out.Write('ready'); Start-Sleep -Seconds 30" },
            input = new string('x', 512 * 1024),
            timeoutMs = 1500
        }).WaitAsync(TimeSpan.FromSeconds(8));

        Assert.True(result.GetProperty("timedOut").GetBoolean());
        Assert.Equal("ready", result.GetProperty("stdout").GetString());
    }

    [Fact]
    public async Task ProcessRunDeadlineAlsoCoversPipesInheritedByAnExitedParentsChild()
    {
        Directory.CreateDirectory(_directory);
        string pidFile = Path.Combine(_directory, "child.pid");
        string parentPidFile = Path.Combine(_directory, "parent.pid");
        string script = Path.Combine(_directory, "child.ps1");
        File.WriteAllText(script,
            $"[IO.File]::WriteAllText('{pidFile.Replace("'", "''")}', $PID.ToString()); " +
            "[Console]::Out.Write('child-ready'); Start-Sleep -Seconds 15",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        string command = $"[IO.File]::WriteAllText('{parentPidFile.Replace("'", "''")}', $PID.ToString()); " +
            $"Start-Process -FilePath '{PowerShellPath.Replace("'", "''")}' " +
            $"-ArgumentList '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script.Replace("'", "''")}\"' -NoNewWindow";
        var elapsed = Stopwatch.StartNew();
        try
        {
            Task<JsonElement> runTask = RunAsync(new
            {
                file = PowerShellPath,
                args = new[] { "-NoProfile", "-NonInteractive", "-Command", command },
                timeoutMs = 5000
            });
            // Prove the child is running and the parent has exited, so this
            // exercises inherited pipes rather than a slow parent startup.
            await WaitForPidFileAsync(pidFile);
            int parentId = await WaitForPidFileAsync(parentPidFile);
            try
            {
                using Process parent = Process.GetProcessById(parentId);
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (ArgumentException)
            {
                // The parent already exited before its handle was acquired.
            }
            JsonElement result = await runTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(result.GetProperty("timedOut").GetBoolean());
            Assert.Contains("child-ready", result.GetProperty("stdout").GetString());
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
        }
        finally
        {
            // The helper is created by this test and records its own PID before
            // writing output. Never enumerate or stop unrelated processes.
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), out int processId))
            {
                try
                {
                    using Process child = Process.GetProcessById(processId);
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync();
                    }
                }
                catch (ArgumentException)
                {
                }
            }
        }
    }

    [Theory]
    [InlineData("Ctrl+9999")]
    [InlineData("Alt+-1")]
    public void HotkeyRejectsUndefinedNumericKeys(string text)
    {
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse(text));
        Assert.Throws<FormatException>(() => HotkeyGesture.FromInput((Key)9999, ModifierKeys.Control));
    }

    private static string PowerShellPath => Path.Combine(
        Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static async Task<JsonElement> RunAsync(object arguments)
    {
        JsonElement args = JsonSerializer.SerializeToElement(arguments);
        return JsonSerializer.SerializeToElement(await HtmlWidgetCanvasWindow.RunWidgetProcessAsync(args));
    }

    private static async Task<int> WaitForPidFileAsync(string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(4))
        {
            if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path), out int pid))
                return pid;
            await Task.Delay(25);
        }
        throw new TimeoutException("The authored process helper did not become ready.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
