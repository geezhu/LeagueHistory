using System.Diagnostics;
using System.IO.Compression;
using League.Controls;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Managers
{
    /// <summary>
    /// 管理应用程序配置、版本更新和设置
    /// </summary>
    public class ConfigUpdateManager
    {
        private readonly FormMain _mainForm; // 新增：保存主窗体引用

        /// <summary>
        /// 增加超时时间（连接 + 整体）
        /// </summary>
        private static readonly HttpClient http = new HttpClient(
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                EnableMultipleHttp2Connections = true,
            }
        )
        {
            Timeout = TimeSpan.FromSeconds(120),
        };

        private LeagueConfig? _config;

        // 修改构造函数（或加一个重载）
        public ConfigUpdateManager(FormMain mainForm)
        {
            _mainForm = mainForm;
        }

        static ConfigUpdateManager()
        {
            // GitHub 要求加 User-Agent，否则会被限流
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory-AutoUpdater/1.0");
        }

        #region 版本更新管理
        /// <summary>
        /// 程序启动时调用即可（你已经在 FormMain_Load 里调用了）
        /// </summary>
        public async Task CheckForUpdates()
        {
            try
            {
                string localVerStr = VersionInfo.GetLocalVersion();
                var localVer = VersionInfo.Parse(localVerStr);

                // 调用 GitHub API 获取 latest release
                string json = await http.GetStringAsync(
                    "https://api.github.com/repos/mizi6654/LeagueHistory/releases/latest"
                );

                var release = JObject.Parse(json);

                string tag = release["tag_name"]?.ToString() ?? "";
                var remoteVer = VersionInfo.Parse(tag);

                // 没有新版本
                if (remoteVer <= localVer)
                {
                    Services.AppLog.Shared.Info(
                        Services.LogCategory.Update,
                        $"已是最新版本：本机 {localVerStr}，上游 {tag}"
                    );
                    return;
                }

                string changelog = release["body"]?.ToString()?.Replace("\r\n", "\n") ?? "无更新日志";
                string releaseName = release["name"]?.ToString() ?? tag;

                string zipUrl = FindZipAsset(release);

                if (string.IsNullOrEmpty(zipUrl))
                {
                    Services.AppLog.Shared.Warn(Services.LogCategory.Update, $"上游 {tag} 没有 zip 发布包");
                    MessageBox.Show(
                        "检测到新版本，但未找到 zip 下载地址，请手动更新。",
                        "更新提示",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

                // 弹窗确认
                string msg =
                    $"发现新版本：{releaseName}\n\n"
                    + $"当前版本：{localVerStr}\n"
                    + $"更新内容：\n{changelog}\n\n"
                    + $"是否立即下载并自动更新？（更新后程序将自动重启）";

                if (
                    MessageBox.Show(msg, "软件更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes
                )
                {
                    Services.AppLog.Shared.Info(Services.LogCategory.Update, $"发现上游 {tag}，用户选择不更新");
                    return;
                }

                // 开始更新
                Services.AppLog.Shared.Info(Services.LogCategory.Update, $"开始更新：{localVerStr} → {tag}");
                await PerformAutoUpdate(zipUrl, tag.TrimStart('v', 'V'));
            }
            catch (Exception ex)
            {
                // 不打扰用户，只写进日志
                Services.AppLog.Shared.Warn(Services.LogCategory.Update, "检查更新失败", ex);
            }
        }

        // 找 zip 资源（你每次只上传一个 zip，名字随意，只要后缀是 .zip）
        private static string FindZipAsset(JObject release)
        {
            var assets = release["assets"] as JArray;
            if (assets == null)
            {
                return null;
            }

            foreach (var asset in assets)
            {
                string name = asset["name"]?.ToString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return asset["browser_download_url"]?.ToString();
                }
            }
            return null;
        }

        private async Task PerformAutoUpdate(string zipUrl, string newVersion)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LeagueUpdaterTemp");
            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractPath = Path.Combine(tempDir, "extract");
            ProgressForm progressForm = null;

            try
            {
                progressForm = new ProgressForm();
                progressForm.Owner = _mainForm;
                progressForm.Show(_mainForm);

                await Task.Delay(3000);

                await Task.Run(async () =>
                {
                    PrepareUpdateDirectories(tempDir, extractPath);
                    await DownloadUpdateAsync(zipUrl, zipPath, progressForm);

                    progressForm.Invoke(() => progressForm.UpdateStatus("下载完成，正在解压..."));
                    ExtractUpdate(zipPath, extractPath);

                    progressForm.Invoke(() => progressForm.UpdateStatus("解压完成，正在生成更新脚本..."));
                    await StartUpdateScriptAsync(tempDir, extractPath, newVersion);
                    Services.AppLog.Shared.Info(
                        Services.LogCategory.Update,
                        $"更新包已解压，启动更新脚本：{newVersion}"
                    );
                    await Services.AppLog.Shared.FlushAsync();

                    progressForm.Invoke(() =>
                    {
                        progressForm.Complete("更新完成，正在重启程序...");
                        Application.Exit();
                    });
                });
            }
            catch (Exception ex)
            {
                progressForm?.Invoke(() => progressForm.Complete($"更新失败: {ex.Message}"));
                Services.AppLog.Shared.Error(Services.LogCategory.Update, "自动更新失败", ex);
            }
        }

        // 清空并创建临时目录
        private static void PrepareUpdateDirectories(string tempDir, string extractPath)
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // 上次残留的文件可能被占用；下载会覆盖 update.zip，解压会逐个替换文件。
                }
            }
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(extractPath);
        }

        /// <summary>
        /// 下载更新包并显示进度；网络或写盘失败最多重试 3 次，每次等待更久。
        /// </summary>
        private async Task DownloadUpdateAsync(string zipUrl, string zipPath, ProgressForm progressForm)
        {
            int retryCount = 0;
            const int maxDownloadRetries = 3;
            bool downloadSuccess = false;

            while (retryCount < maxDownloadRetries && !downloadSuccess)
            {
                try
                {
                    await DownloadUpdateOnceAsync(zipUrl, zipPath, progressForm);
                    downloadSuccess = true;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                {
                    retryCount++;
                    Services.AppLog.Shared.Warn(
                        Services.LogCategory.Update,
                        $"下载更新包失败（第 {retryCount}/{maxDownloadRetries} 次）",
                        ex
                    );
                    if (retryCount >= maxDownloadRetries)
                    {
                        throw;
                    }

                    progressForm.Invoke(() =>
                        progressForm.UpdateStatus(
                            $"下载失败，正在第 {retryCount}/{maxDownloadRetries} 次重试...\r\n",
                            null
                        )
                    );
                    await Task.Delay(3000 * retryCount);
                }
            }

            if (!downloadSuccess)
            {
                throw new Exception("多次尝试后仍无法下载更新包");
            }
        }

        private async Task DownloadUpdateOnceAsync(string zipUrl, string zipPath, ProgressForm progressForm)
        {
            var randomQuery = "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string freshZipUrl = zipUrl + randomQuery;

            using var request = new HttpRequestMessage(HttpMethod.Get, freshZipUrl);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
            };

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault(-1L);
            await using var remoteStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long downloadedBytes = 0;
            int bytesRead;

            while ((bytesRead = await remoteStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    int percent = (int)((downloadedBytes * 100L) / totalBytes);
                    progressForm.Invoke(() =>
                        progressForm.UpdateStatus(
                            $"从 GitHub 下载中... {percent}% ({downloadedBytes / 1024 / 1024} MB)\r\n"
                                + "（速度受网络影响，可能较慢，请勿关闭窗口）",
                            percent
                        )
                    );
                }
            }
        }

        private static void ExtractUpdate(string zipPath, string extractPath)
        {
            string extractRoot = Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar;
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                string destPath = Path.GetFullPath(
                    Path.Combine(extractPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar))
                );
                // 拒绝 ../ 等指向解压目录之外的条目
                if (!destPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("更新包包含解压目录之外的路径：" + entry.FullName);
                }

                string destDir = Path.GetDirectoryName(destPath)!;

                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                entry.ExtractToFile(destPath);
            }
        }

        /// <summary>
        /// 生成带配置备份恢复的更新脚本并启动；脚本等程序退出后覆盖文件、恢复配置并重新启动。
        /// </summary>
        private static async Task StartUpdateScriptAsync(string tempDir, string extractPath, string newVersion)
        {
            string batPath = Path.Combine(tempDir, "update.bat");
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exeName = Path.GetFileName(Application.ExecutablePath);

            string batContent =
                $@"
@echo off
title 更新中，请勿关闭...
timeout /t 2 /nobreak >nul

REM 备份用户配置文件
if exist ""%LH_UPDATE_APP%\LeagueConfig.json"" (
    copy /y ""%LH_UPDATE_APP%\LeagueConfig.json"" ""%LH_UPDATE_TEMP%\LeagueConfig.json.bak"" >nul
)

REM 全量覆盖更新文件
xcopy ""%LH_UPDATE_EXTRACT%\*.*"" ""%LH_UPDATE_APP%\"" /s /e /y /q /i /h

REM 恢复用户配置文件（防止被新版本的json覆盖）
if exist ""%LH_UPDATE_TEMP%\LeagueConfig.json.bak"" (
    copy /y ""%LH_UPDATE_TEMP%\LeagueConfig.json.bak"" ""%LH_UPDATE_APP%\LeagueConfig.json"" >nul
)

echo {newVersion} > ""%LH_UPDATE_APP%\version.txt""

start """" /d ""%LH_UPDATE_APP%"" ""%LH_UPDATE_EXE%""
rd /s /q ""%LH_UPDATE_TEMP%""
exit";

            // cmd 按系统代码页读取批处理：中文用户名（临时目录）或安装目录直接写进脚本会被读错，
            // 路径改由环境变量传给子进程。不写 BOM，否则首行 @echo off 无法识别。
            Environment.SetEnvironmentVariable("LH_UPDATE_APP", appDir);
            Environment.SetEnvironmentVariable("LH_UPDATE_EXTRACT", extractPath);
            Environment.SetEnvironmentVariable("LH_UPDATE_TEMP", tempDir);
            Environment.SetEnvironmentVariable("LH_UPDATE_EXE", exeName);
            await File.WriteAllTextAsync(batPath, batContent, new System.Text.UTF8Encoding(false));

            // 启动 BAT 并退出当前程序
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                }
            );
        }
        #endregion

        #region 配置管理
        /// <summary>
        /// 加载应用程序配置
        /// </summary>
        public LeagueConfig LoadConfig()
        {
            if (_config == null)
            {
                _config = LeagueConfigManager.Load();
            }
            return _config;
        }

        /// <summary>
        /// 保存应用程序配置
        /// </summary>
        public void SaveConfig()
        {
            if (_config != null)
            {
                LeagueConfigManager.Save(_config);
            }
        }

        #endregion

        #region 预热功能
        /// <summary>
        /// 预热UI组件（减少首次加载延迟）
        /// </summary>
        public void PreWarmUiComponents()
        {
            PreWarmControls();
            PreWarmResourcesAsync();
        }

        /// <summary>
        /// 预热控件
        /// </summary>
        private void PreWarmControls()
        {
            try
            {
                using (var dummy = new MatchTabPageContent())
                {
                    dummy.CreateControl();
                    using (var g = dummy.CreateGraphics()) { }
                }
                //Debug.WriteLine("UI 预热完成（控件实例化）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UI 预热失败: {ex}");
            }
        }

        /// <summary>
        /// 异步预热资源
        /// </summary>
        private void PreWarmResourcesAsync()
        {
            Task.Run(async () =>
            {
                try
                {
                    // 预热LCU客户端
                    await PreWarmLcuClient();

                    // 预热资源加载器
                    await PreWarmResourceLoader();

                    //Debug.WriteLine("后台预热完成（异步方法 + 资源）");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"后台预热失败: {ex}");
                }
            });
        }

        /// <summary>
        /// 预热LCU客户端
        /// </summary>
        private async Task PreWarmLcuClient()
        {
            try
            {
                await Globals.lcuClient.GetSummonerByNameAsync("fake_prewarm#0000");
            }
            catch { }

            try
            {
                await Globals.lcuClient.GetCurrentRankedStatsAsync("fake-prewarm-puuid");
            }
            catch { }
        }

        /// <summary>
        /// 预热资源加载器
        /// </summary>
        private async Task PreWarmResourceLoader()
        {
            try
            {
                await Globals.resLoading.GetChampionInfoAsync(1);
                await Profileicon.GetProfileIconAsync(1);
            }
            catch { }
        }
        #endregion
    }
}
