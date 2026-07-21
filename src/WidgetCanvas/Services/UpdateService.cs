#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCanvas.Services
{
    internal enum UpdateChannel
    {
        Gitee,
        GitHub
    }

    internal sealed record UpdateCheckResult(
        Version CurrentVersion,
        Version LatestVersion,
        string ReleaseUrl,
        string DownloadUrl,
        string ChecksumUrl,
        UpdateChannel Channel)
    {
        public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
    }

    internal sealed class UpdateService
    {
        public const string ProjectUrl = "https://github.com/shuimowang/WidgetCanvas";
        public const string ReleasesUrl = ProjectUrl + "/releases";
        public const string FeedbackUrl = ProjectUrl + "/issues/new";
        public const string GiteeProjectUrl = "https://gitee.com/shuimowang/WidgetCanvas";
        public const string GiteeReleasesUrl = GiteeProjectUrl + "/releases";
        private const string GitHubLatestReleaseUrl = ReleasesUrl + "/latest";
        private const string GiteeLatestReleaseApiUrl =
            "https://gitee.com/api/v5/repos/shuimowang/WidgetCanvas/releases/latest";
        private const string GiteeApiRoot =
            "https://gitee.com/api/v5/repos/shuimowang/WidgetCanvas/releases/";
        private const string ExecutableAssetName = "WidgetCanvas-win-x64.exe";
        private const string ChecksumAssetName = ExecutableAssetName + ".sha256";
        private const long MaximumExecutableBytes = 200L * 1024 * 1024;

        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static Version CurrentVersion =>
            typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

        public async Task<UpdateCheckResult> CheckAsync(
            UpdateChannel preferredChannel = UpdateChannel.Gitee,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return preferredChannel == UpdateChannel.Gitee
                    ? await CheckGiteeAsync(cancellationToken)
                    : await CheckGitHubAsync(cancellationToken);
            }
            catch (Exception first) when (IsChannelFailure(first, cancellationToken))
            {
                try
                {
                    return preferredChannel == UpdateChannel.Gitee
                        ? await CheckGitHubAsync(cancellationToken)
                        : await CheckGiteeAsync(cancellationToken);
                }
                catch (Exception second) when (IsChannelFailure(second, cancellationToken))
                {
                    throw new InvalidOperationException(
                        "Gitee 与 GitHub 更新渠道均不可用。\n\n" +
                        $"首选渠道：{GetChannelName(preferredChannel)}\n" +
                        $"{GetChannelName(preferredChannel)}：{first.Message}\n" +
                        $"{GetChannelName(preferredChannel == UpdateChannel.Gitee ? UpdateChannel.GitHub : UpdateChannel.Gitee)}：{second.Message}",
                        new AggregateException(first, second));
                }
            }
        }

        private async Task<UpdateCheckResult> CheckGitHubAsync(CancellationToken cancellationToken)
        {
            // GitHub REST API gives unauthenticated clients a small shared rate limit.
            // The public /releases/latest page redirects to the current release tag and
            // provides everything the updater needs without consuming that quota.
            using HttpResponseMessage response = await HttpClient.GetAsync(
                GitHubLatestReleaseUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"检查更新失败：GitHub 返回 HTTP {(int)response.StatusCode}（{response.ReasonPhrase}）。");
            }

            Uri releaseUri = response.RequestMessage?.RequestUri
                ?? throw new InvalidDataException("无法确定 GitHub 最新版本地址。");
            return ParseLatestReleaseUri(releaseUri, CurrentVersion);
        }

        private async Task<UpdateCheckResult> CheckGiteeAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                GiteeLatestReleaseApiUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Gitee 返回 HTTP {(int)response.StatusCode}（{response.ReasonPhrase}）。");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            string tag = GetRequiredString(root, "tag_name", "Gitee Release 缺少版本号。");
            Version latestVersion = ParseVersion(tag, "Gitee Release");
            string downloadUrl = FindAssetUrl(root, ExecutableAssetName);
            string checksumUrl = FindAssetUrl(root, ChecksumAssetName);

            if ((downloadUrl.Length == 0 || checksumUrl.Length == 0) &&
                root.TryGetProperty("id", out JsonElement idElement) &&
                idElement.TryGetInt64(out long releaseId))
            {
                using HttpResponseMessage assetsResponse = await HttpClient.GetAsync(
                    GiteeApiRoot + releaseId.ToString(CultureInfo.InvariantCulture) + "/attach_files",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (assetsResponse.IsSuccessStatusCode)
                {
                    await using Stream assetsStream = await assetsResponse.Content.ReadAsStreamAsync(cancellationToken);
                    using JsonDocument assetsDocument = await JsonDocument.ParseAsync(
                        assetsStream,
                        cancellationToken: cancellationToken);
                    if (downloadUrl.Length == 0)
                        downloadUrl = FindAssetUrl(assetsDocument.RootElement, ExecutableAssetName, releaseId);
                    if (checksumUrl.Length == 0)
                        checksumUrl = FindAssetUrl(assetsDocument.RootElement, ChecksumAssetName, releaseId);
                }
            }

            if (downloadUrl.Length == 0 || checksumUrl.Length == 0)
                throw new InvalidDataException("Gitee 最新发行版缺少自动更新文件。");

            return new UpdateCheckResult(
                NormalizeVersion(CurrentVersion),
                NormalizeVersion(latestVersion),
                GiteeProjectUrl + "/releases/tag/" + Uri.EscapeDataString(tag),
                downloadUrl,
                checksumUrl,
                UpdateChannel.Gitee);
        }

        internal static UpdateCheckResult ParseLatestReleaseUri(Uri releaseUri, Version currentVersion)
        {
            ArgumentNullException.ThrowIfNull(releaseUri);
            const string releaseTagPath = "/releases/tag/";
            int tagIndex = releaseUri.AbsolutePath.IndexOf(releaseTagPath, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || tagIndex < 0)
                throw new InvalidDataException("GitHub 没有返回可用的正式版本。");

            string tag = Uri.UnescapeDataString(releaseUri.AbsolutePath[(tagIndex + releaseTagPath.Length)..])
                .Trim('/');
            Version latestVersion = ParseVersion(tag, "GitHub Release");

            string releaseUrl = ProjectUrl + releaseTagPath + Uri.EscapeDataString(tag);
            string assetRoot = ProjectUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
            return new UpdateCheckResult(
                NormalizeVersion(currentVersion),
                NormalizeVersion(latestVersion),
                releaseUrl,
                assetRoot + ExecutableAssetName,
                assetRoot + ChecksumAssetName,
                UpdateChannel.GitHub);
        }

        private static Version ParseVersion(string tag, string source)
        {
            if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out Version? version))
                throw new InvalidDataException(source + " 的版本号无效：" + tag);
            return version;
        }

        private static string GetRequiredString(JsonElement root, string name, string error)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!.Trim();
            }
            throw new InvalidDataException(error);
        }

        internal static string FindAssetUrl(
            JsonElement root,
            string assetName,
            long? releaseId = null)
        {
            JsonElement assets = root;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("assets", out JsonElement nestedAssets))
            {
                assets = nestedAssets;
            }
            if (assets.ValueKind != JsonValueKind.Array)
                return string.Empty;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object ||
                    !asset.TryGetProperty("name", out JsonElement name) ||
                    !string.Equals(name.GetString(), assetName, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (string propertyName in new[] { "browser_download_url", "download_url" })
                {
                    if (asset.TryGetProperty(propertyName, out JsonElement url) &&
                        url.ValueKind == JsonValueKind.String &&
                        Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? parsed) &&
                        parsed.Scheme == Uri.UriSchemeHttps)
                    {
                        return parsed.AbsoluteUri;
                    }
                }
                if (releaseId.HasValue && asset.TryGetProperty("id", out JsonElement id) &&
                    id.TryGetInt64(out long assetId))
                {
                    return GiteeApiRoot + releaseId.Value.ToString(CultureInfo.InvariantCulture) +
                           "/attach_files/" + assetId.ToString(CultureInfo.InvariantCulture) + "/download";
                }
            }
            return string.Empty;
        }

        internal static string GetChannelName(UpdateChannel channel) =>
            channel == UpdateChannel.Gitee ? "Gitee" : "GitHub";

        private static bool IsChannelFailure(Exception exception, CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or InvalidDataException or JsonException or TaskCanceledException;

        public async Task<string> DownloadAsync(
            UpdateCheckResult update,
            CancellationToken cancellationToken = default)
        {
            if (!update.IsUpdateAvailable)
                throw new InvalidOperationException("当前已经是最新版本。");
            if (string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.ChecksumUrl))
                throw new InvalidDataException("此 Release 缺少自动更新文件，请改为从项目页面手动下载。");

            Directory.CreateDirectory(AppPaths.UpdatesFolder);
            string expectedHash = await HttpClient.GetStringAsync(update.ChecksumUrl, cancellationToken);
            expectedHash = expectedHash.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? throw new InvalidDataException("更新校验文件内容为空。");
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("更新校验值格式无效。");

            string finalPath = Path.Combine(
                AppPaths.UpdatesFolder,
                $"WidgetCanvas-{update.LatestVersion}.exe");
            string tempPath = finalPath + ".download";
            if (File.Exists(finalPath) &&
                string.Equals(
                    await ComputeFileHashAsync(finalPath, cancellationToken),
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return finalPath;
            }

            try
            {
                using HttpResponseMessage response = await HttpClient.GetAsync(
                    update.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length > MaximumExecutableBytes)
                    throw new InvalidDataException("更新文件超过 200 MB，已停止下载。");

                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > MaximumExecutableBytes)
                        throw new InvalidDataException("更新文件超过 200 MB，已停止下载。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer, 0, read);
                }
                await output.FlushAsync(cancellationToken);
                string actualHash = Convert.ToHexString(hasher.GetHashAndReset());
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新文件 SHA-256 校验失败，文件没有被安装。");
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return finalPath;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetCanvas/" + CurrentVersion.ToString(3));
            return client;
        }

        private static Version NormalizeVersion(Version version) =>
            new(version.Major, version.Minor, Math.Max(0, version.Build));

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static async Task<string> ComputeFileHashAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }
    }

    internal static class UpdateInstaller
    {
        private const string ApplyOption = "--apply-update";

        public static bool IsApplyRequest(string[] args) =>
            args.Length >= 4 && string.Equals(args[0], ApplyOption, StringComparison.OrdinalIgnoreCase);

        public static void Start(string stagedExecutable, string targetExecutable)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stagedExecutable);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetExecutable);
            var startInfo = new ProcessStartInfo
            {
                FileName = stagedExecutable,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(ApplyOption);
            startInfo.ArgumentList.Add(Path.GetFullPath(targetExecutable));
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(UpdateService.CurrentVersion.ToString(3));
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新程序。");
        }

        public static int Apply(string[] args)
        {
            try
            {
                string source = Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法确定更新程序路径。");
                string target = Path.GetFullPath(args[1]);
                if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int processId))
                    throw new ArgumentException("更新进程参数无效。");
                if (!string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("更新目标必须是 EXE 文件。");
                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("更新程序不能覆盖正在运行的自身。");

                try
                {
                    using Process oldProcess = Process.GetProcessById(processId);
                    if (!oldProcess.WaitForExit(60000))
                        throw new TimeoutException("等待旧版本退出超时。");
                }
                catch (ArgumentException)
                {
                }

                ReplaceExecutable(source, target);
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return 0;
            }
            catch (Exception ex)
            {
                WriteFailure(ex);
                System.Windows.MessageBox.Show(
                    "自动更新没有完成。\n\n" + ex.Message,
                    "WidgetCanvas 更新",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return 1;
            }
        }

        internal static void ReplaceExecutable(string source, string target)
        {
            string replacement = target + ".new";
            try
            {
                File.Copy(source, replacement, overwrite: true);
                File.Move(replacement, target, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(replacement))
                        File.Delete(replacement);
                }
                catch
                {
                }
                throw;
            }
        }

        private static void WriteFailure(Exception exception)
        {
            try
            {
                AppPaths.EnsureCreated();
                File.WriteAllText(
                    Path.Combine(AppPaths.LogsFolder, $"update-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
                    exception.ToString());
            }
            catch
            {
            }
        }
    }
}
