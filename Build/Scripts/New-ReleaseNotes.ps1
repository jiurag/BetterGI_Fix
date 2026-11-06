[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $repositoryRoot

try
{
    $revision = if ($env:GITHUB_REF_TYPE -eq "tag") { "HEAD^" } else { "HEAD" }
    $previousTag = & git describe --tags --abbrev=0 --match "v*" $revision 2>$null
    if ($LASTEXITCODE -ne 0)
    {
        $previousTag = $null
    }

    $range = if ($previousTag) { "$previousTag..HEAD" } else { "HEAD" }
    $commits = @(& git log $range --pretty=format:"- %s (`%h`)" --reverse)
    if ($LASTEXITCODE -ne 0)
    {
        throw "读取发布提交记录失败。"
    }

    $commitSha = if ($env:GITHUB_SHA) {
        $env:GITHUB_SHA.Substring(0, [Math]::Min(7, $env:GITHUB_SHA.Length))
    } else {
        (& git rev-parse --short=7 HEAD).Trim()
    }

    $commitLines = if ($commits.Count -gt 0) {
        $commits -join "`n"
    } else {
        "- 更新软件功能与配置。"
    }

    $releaseNotes = @"
# BetterGI_Fix v$Version

## 本次变更

$commitLines

## 发布文件

- `BetterGI.Install.*.exe`：安装版
- `BetterGI_*.zip`：便携版

## 说明

帧率解锁、去雾和 FOV 调整会修改游戏运行状态。游戏更新后相关功能可能失效，账号风险无法保证。
"@

    Set-Content -LiteralPath $OutputPath -Value $releaseNotes -Encoding utf8NoBOM
    Write-Host "已生成发布说明：$OutputPath"
}
finally
{
    Pop-Location
}
