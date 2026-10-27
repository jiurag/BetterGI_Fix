using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using Semver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Service;

public class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly IConfigService _configService;
    /// <summary>
    /// 串行化 GitHub Release 缓存刷新，避免多个并发检查重复请求接口。
    /// </summary>
    private readonly SemaphoreSlim _releaseCacheLock = new(1, 1);

    /// <summary>
    /// 本分支的 GitHub 仓库标识。
    /// </summary>
    private const string GitHubRepository = "jiurag/BetterGI_Fix";
    /// <summary>
    /// 本分支 GitHub Release 列表接口。
    /// </summary>
    private const string GitHubReleasesApiUrl =
        "https://api.github.com/repos/" + GitHubRepository + "/releases?per_page=30";
    /// <summary>
    /// 手动下载页对应的本分支 Releases 地址。
    /// </summary>
    private const string DownloadPageUrl = "https://github.com/" + GitHubRepository + "/releases";
    /// <summary>
    /// GitHub Release 列表在内存中的缓存时长。
    /// </summary>
    private static readonly TimeSpan ReleaseCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最近一次成功获取并解析的 Release 列表。
    /// </summary>
    private IReadOnlyList<GitHubReleaseInfo>? _cachedReleases;
    /// <summary>
    /// Release 缓存的失效时间。
    /// </summary>
    private DateTimeOffset _releaseCacheExpiresAt;

    public AllConfig Config { get; set; }

    public UpdateService(IConfigService configService)
    {
        _logger = App.GetLogger<UpdateService>();
        _configService = configService;
        Config = _configService.Get();
    }


    /// <summary>
    /// Please call me in main thread
    /// </summary>
    /// <param name="option"></param>
    public async Task CheckUpdateAsync(UpdateOption option)
    {
        try
        {
#if DEBUG && false
            return;
#endif
            string newVersion = await GetLatestVersionAsync(option);

            if (string.IsNullOrWhiteSpace(newVersion))
            {
                return;
            }

            // ---- 如果是调试模式且手动的检查更新的情况下，强制打开更新窗口 -----
            // 方便调试窗口
            if (RuntimeHelper.IsDebuggerAttached && option.Trigger == UpdateTrigger.Manual)
            {
                await OpenCheckUpdateWindow(option, newVersion);
                return;
            }
            // ---- 如果是调试模式且手动的检查更新的情况下，强制打开更新窗口 -----

            if (!Global.IsNewVersion(newVersion))
            {
                if (option.Trigger == UpdateTrigger.Manual)
                {
                    await ThemedMessageBox.InformationAsync("当前已是最新版本！");
                }

                return;
            }

            if (!string.IsNullOrEmpty(Config.NotShowNewVersionNoticeEndVersion)
                && !Global.IsNewVersion(Config.NotShowNewVersionNoticeEndVersion, newVersion)
                && option.Trigger == UpdateTrigger.Auto)
            {
                return;
            }

            await OpenCheckUpdateWindow(option, newVersion);
        }
        catch (Exception e)
        {
            Debug.WriteLine("获取最新版本信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" +
                            Environment.NewLine + e.Message);
            _logger.LogWarning("获取 BetterGI 最新版本信息失败");
        }
    }

    private async Task OpenCheckUpdateWindow(UpdateOption option, string newVersion)
    {
        CheckUpdateWindow win = new(option)
        {
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = $"发现新版本 {newVersion}",
            UserInteraction = async (sender, button) =>
            {
                CheckUpdateWindow win = (CheckUpdateWindow)sender;

                switch (button)
                {
                    case CheckUpdateWindow.CheckUpdateWindowButton.BackgroundUpdate:
                        // TBD
                        break;

                    case CheckUpdateWindow.CheckUpdateWindowButton.OtherUpdate:
                        Process.Start(new ProcessStartInfo(DownloadPageUrl) { UseShellExecute = true });
                        break;

                    case CheckUpdateWindow.CheckUpdateWindowButton.Update:
                    {
                        // 唤起更新程序
                        string updaterExePath = Global.Absolute("BetterGI.update.exe");
                        if (!File.Exists(updaterExePath))
                        {
                            await ThemedMessageBox.ErrorAsync("更新程序不存在，请选择其他更新方式！");
                            return;
                        }

                        // 启动
                        Process.Start(updaterExePath, "-I");

                        // 退出程序
                        Application.Current.Shutdown();
                    }
                        break;

                    case CheckUpdateWindow.CheckUpdateWindowButton.Ignore:
                        Config.NotShowNewVersionNoticeEndVersion = newVersion;
                        win.Close();
                        break;

                    case CheckUpdateWindow.CheckUpdateWindowButton.Cancel:
                        win.ShowUpdateStatus = false;
                        win.Close();
                        break;
                }
            }
        };

        if (option.Channel == UpdateChannel.Stable)
        {
            win.NavigateToHtml(await GetReleaseMarkdownHtmlAsync(option, newVersion));
        }

        win.ShowDialog();
    }

    private async Task<string> GetLatestVersionAsync(UpdateOption option)
    {
        GitHubReleaseInfo? release = await GetLatestReleaseAsync(option.Channel);
        return release?.Version ?? string.Empty;
    }

    /// <summary>
    /// 根据更新渠道获取本分支最新可用的 Release。
    /// </summary>
    /// <param name="channel">更新渠道。</param>
    /// <returns>最新可用的 Release；没有符合条件的数据时返回 <see langword="null"/>。</returns>
    private async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(UpdateChannel channel)
    {
        IReadOnlyList<GitHubReleaseInfo> releases = await GetReleasesAsync();
        List<GitHubReleaseInfo> publishedReleases = releases
            .Where(x => !x.Draft)
            .OrderByDescending(x => SemVersion.Parse(x.Version))
            .ToList();

        if (channel == UpdateChannel.Alpha)
        {
            // Alpha 渠道始终跟随最新发布版本，避免新的正式版被旧预发布版覆盖。
            return publishedReleases.FirstOrDefault();
        }

        // 本分支当前可能只有预发布包；没有正式版时回退到最新发布包，
        // 保证从第三方分支切换后仍能检查本仓库的更新。
        return publishedReleases.FirstOrDefault(x => !x.IsPrerelease)
               ?? publishedReleases.FirstOrDefault();
    }

    /// <summary>
    /// 获取并缓存本分支的 GitHub Release 列表。
    /// </summary>
    /// <returns>已按版本解析的 Release 列表。</returns>
    private async Task<IReadOnlyList<GitHubReleaseInfo>> GetReleasesAsync()
    {
        if (_cachedReleases != null && DateTimeOffset.UtcNow < _releaseCacheExpiresAt)
        {
            return _cachedReleases;
        }

        await _releaseCacheLock.WaitAsync();
        try
        {
            if (_cachedReleases != null && DateTimeOffset.UtcNow < _releaseCacheExpiresAt)
            {
                return _cachedReleases;
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BetterGI_Fix");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await httpClient.GetAsync(GitHubReleasesApiUrl);
            response.EnsureSuccessStatusCode();

            List<GitHubReleaseDto>? releaseDtos =
                await response.Content.ReadFromJsonAsync<List<GitHubReleaseDto>>();
            if (releaseDtos == null)
            {
                return [];
            }

            _cachedReleases = releaseDtos
                .Where(x => !string.IsNullOrWhiteSpace(x.TagName))
                .Select(x =>
                {
                    string version = NormalizeVersion(x.TagName);
                    return new GitHubReleaseInfo(
                        version,
                        string.IsNullOrWhiteSpace(x.Name) ? x.TagName : x.Name,
                        x.Body ?? string.Empty,
                        x.Prerelease,
                        x.Draft);
                })
                .Where(x => IsSemanticVersion(x.Version))
                .ToList();
            _releaseCacheExpiresAt = DateTimeOffset.UtcNow.Add(ReleaseCacheDuration);
            return _cachedReleases;
        }
        finally
        {
            _releaseCacheLock.Release();
        }
    }

    /// <summary>
    /// 将 GitHub Release 标签规范化成版本号。
    /// </summary>
    /// <param name="tagName">GitHub Release 标签。</param>
    /// <returns>移除可选的 <c>v</c> 前缀后的版本号。</returns>
    private static string NormalizeVersion(string tagName)
    {
        return tagName.Trim().TrimStart('v', 'V');
    }

    /// <summary>
    /// 判断文本是否可以被解析为语义化版本。
    /// </summary>
    /// <param name="version">待校验的版本文本。</param>
    /// <returns>可以解析时返回 <see langword="true"/>。</returns>
    private static bool IsSemanticVersion(string version)
    {
        try
        {
            SemVersion.Parse(version);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取指定渠道最新 Release 的更新日志 HTML。
    /// </summary>
    /// <param name="option">当前更新选项。</param>
    /// <param name="newVersion">准备展示的版本号。</param>
    /// <returns>可用于 WebView 展示的更新日志 HTML。</returns>
    private async Task<string> GetReleaseMarkdownHtmlAsync(UpdateOption option, string newVersion)
    {
        try
        {
            GitHubReleaseInfo? release = await GetLatestReleaseAsync(option.Channel);
            if (release != null
                && string.Equals(release.Version, newVersion, StringComparison.OrdinalIgnoreCase))
            {
                string md = $"# {release.Name}{new string('\n', 2)}{release.Body}";

                md = WebUtility.HtmlEncode(md);
                string md2html = File.ReadAllText(Global.Absolute(@"Assets\Strings\md2html.html"), Encoding.UTF8);
                var html = md2html.Replace("{{content}}", md);

                return html;
            }
        }
        catch (Exception e)
        {
            _ = e;
        }

        return GetReleaseMarkdownHtmlFallback();
    }

    /// <summary>
    /// 从 GitHub Release 接口整理出的更新信息。
    /// </summary>
    /// <param name="Version">规范化后的版本号。</param>
    /// <param name="Name">Release 显示名称。</param>
    /// <param name="Body">Release 更新日志正文。</param>
    /// <param name="IsPrerelease">是否为预发布版本。</param>
    /// <param name="Draft">是否为草稿。</param>
    private sealed record GitHubReleaseInfo(
        string Version,
        string Name,
        string Body,
        bool IsPrerelease,
        bool Draft);

    /// <summary>
    /// GitHub Release 接口中本服务需要读取的字段。
    /// </summary>
    private sealed class GitHubReleaseDto
    {
        /// <summary>
        /// Release 标签。
        /// </summary>
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        /// <summary>
        /// Release 显示名称。
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        /// <summary>
        /// Release 更新日志正文。
        /// </summary>
        [JsonPropertyName("body")]
        public string? Body { get; init; }

        /// <summary>
        /// 是否为预发布版本。
        /// </summary>
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        /// <summary>
        /// 是否为草稿。
        /// </summary>
        [JsonPropertyName("draft")]
        public bool Draft { get; init; }
    }

    private string GetReleaseMarkdownHtmlFallback()
    {
        return
            """
            <!DOCTYPE html>
            <html lang="zh">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>更新日志</title>
                <style>
                    body {
                        background-color: #212121;
                        color: white;
                        font-family: Arial, sans-serif;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        margin: 0;
                    }
                    .message {
                        text-align: center;
                        font-size: 20px;
                    }
                </style>
            </head>
            <body>
                <div class="message">
                    获取更新日志失败，请自行选择是否更新！
                </div>
            </body>
            </html>
            """;
    }
}
