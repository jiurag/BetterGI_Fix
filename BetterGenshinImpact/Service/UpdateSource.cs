namespace BetterGenshinImpact.Service;

/// <summary>
/// 本分支应用更新的唯一来源配置。
/// </summary>
public static class UpdateSource
{
    /// <summary>
    /// 本分支的 GitHub 仓库标识。
    /// </summary>
    public const string GitHubRepository = "jiurag/BetterGI_Fix";

    /// <summary>
    /// 本分支 GitHub Release 列表接口。
    /// </summary>
    public const string GitHubReleasesApiUrl =
        "https://api.github.com/repos/" + GitHubRepository + "/releases?per_page=30";

    /// <summary>
    /// 手动下载页对应的本分支 Releases 地址。
    /// </summary>
    public const string DownloadPageUrl =
        "https://github.com/" + GitHubRepository + "/releases";
}
