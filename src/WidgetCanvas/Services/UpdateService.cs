#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCanvas.Services
{
    internal sealed record UpdateCheckResult(
        Version CurrentVersion,
        Version LatestVersion,
        string ReleaseUrl,
        string DownloadUrl,
        string ChecksumUrl)
    {
        public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
    }

    internal sealed class UpdateService
    {
        public const string ProjectUrl = "https://github.com/shuimowang/WidgetCanvas";
        public const string ReleasesUrl = ProjectUrl + "/releases";
        public const string FeedbackUrl = ProjectUrl + "/issues/new";
        private const string LatestReleaseUrl = ReleasesUrl + "/latest";
        private const string ExecutableAssetName = "WidgetCanvas-win-x64.exe";
        private const string ChecksumAssetName = ExecutableAssetName + ".sha256";
        private const long MaximumExecutableBytes = 200L * 1024 * 1024;

        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static Version CurrentVersion =>
            typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            // GitHub REST API gives unauthenticated clients a small shared rate limit.
            // The public /releases/latest page redirects to the current release tag and
            // provides everything the updater needs without consuming that quota.
            using HttpResponseMessage response = await HttpClient.GetAsync(
                LatestReleaseUrl,
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

        internal static UpdateCheckResult ParseLatestReleaseUri(Uri releaseUri, Version currentVersion)
        {
            ArgumentNullException.ThrowIfNull(releaseUri);
            const string releaseTagPath = "/releases/tag/";
            int tagIndex = releaseUri.AbsolutePath.IndexOf(releaseTagPath, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || tagIndex < 0)
                throw new InvalidDataException("GitHub 没有返回可用的正式版本。");

            string tag = Uri.UnescapeDataString(releaseUri.AbsolutePath[(tagIndex + releaseTagPath.Length)..])
                .Trim('/');
            if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out Version? latestVersion))
                throw new InvalidDataException("GitHub Release 的版本号无效：" + tag);

            string releaseUrl = ProjectUrl + releaseTagPath + Uri.EscapeDataString(tag);
            string assetRoot = ProjectUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
            return new UpdateCheckResult(
                NormalizeVersion(currentVersion),
                NormalizeVersion(latestVersion),
                releaseUrl,
                assetRoot + ExecutableAssetName,
                assetRoot + ChecksumAssetName);
        }

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
