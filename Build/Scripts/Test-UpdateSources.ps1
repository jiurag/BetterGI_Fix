[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$expectedRepository = if ($env:BETTERGI_UPDATE_REPOSITORY) {
    $env:BETTERGI_UPDATE_REPOSITORY
} else {
    "jiurag/BetterGI_Fix"
}

function Assert-Contains
{
    param(
        [string]$Name,
        [string]$Content,
        [string]$Expected
    )

    if (-not $Content.Contains($Expected, [StringComparison]::Ordinal))
    {
        throw "$Name 未包含预期的更新源配置：$Expected"
    }
}

$updateSourcePath = Join-Path $repositoryRoot "BetterGenshinImpact\Service\UpdateSource.cs"
$updateSource = Get-Content -LiteralPath $updateSourcePath -Raw -Encoding utf8
Assert-Contains -Name "C# 更新源" -Content $updateSource -Expected $expectedRepository
Assert-Contains -Name "C# Release API" -Content $updateSource `
    -Expected "https://api.github.com/repos/"
Assert-Contains -Name "C# 手动下载页" -Content $updateSource `
    -Expected "https://github.com/"

$kachinaConfigPath = Join-Path $repositoryRoot "Build\kachina.config.json"
$kachinaConfig = Get-Content -LiteralPath $kachinaConfigPath -Raw -Encoding utf8 |
    ConvertFrom-Json
$githubSource = $kachinaConfig.source |
    Where-Object { $_.id -eq "github" } |
    Select-Object -First 1
if ($null -eq $githubSource)
{
    throw "Kachina 配置缺少 github 更新源。"
}

$expectedReleasePrefix =
    "https://github.com/$expectedRepository/releases/download/"
if (-not $githubSource.uri.StartsWith($expectedReleasePrefix, [StringComparison]::Ordinal))
{
    throw "Kachina 更新器未指向预期的 Releases 地址：$($githubSource.uri)"
}

$workflowPath = Join-Path $repositoryRoot ".github\workflows\publish.yml"
$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding utf8
Assert-Contains -Name "发布工作流更新源" -Content $workflow `
    -Expected "BETTERGI_UPDATE_REPOSITORY: $expectedRepository"
Assert-Contains -Name "Kachina 构建参数" -Content $workflow `
    -Expected 'gen -j 6 -i BetterGI -m metadata.json -o hashed -r ${{ env.BETTERGI_UPDATE_REPOSITORY }}'
Assert-Contains -Name "发布目标提交" -Content $workflow `
    -Expected 'target_commitish: ${{ github.sha }}'
Assert-Contains -Name "发布说明文件" -Content $workflow `
    -Expected 'body_path: ${{ github.workspace }}/release-notes.md'

Write-Host "更新源配置检查通过：$expectedRepository"
