#Requires -Version 5.1
<#
.SYNOPSIS
    检查上游是否有比本地记录更新的源码快照。

.DESCRIPTION
    上游发布节奏不固定，源码快照还比正式版 release 晚一点。
    这个脚本只做一件事：回答"现在该不该同步"，并用退出码告诉 agent。

    退出码：
        0   已是最新（或本地游标 >= 上游 tag）
        10  上游有新快照，应该同步
        1   前置检查失败（remote / 网络 / 文件缺失）

.PARAMETER UpstreamRemote
    上游远端名，默认 upstream。

.PARAMETER CursorFile
    同步游标文件，默认 docs/fork/LAST-SYNCED。

.PARAMETER Json
    输出机器可读的一行 JSON（agent 解析用）。

.EXAMPLE
    powershell -NoProfile -File scripts/check-upstream.ps1
#>
[CmdletBinding()]
param(
    [string]$UpstreamRemote = 'upstream',
    [string]$CursorFile = 'docs/fork/LAST-SYNCED',
    [switch]$Json
)

$ErrorActionPreference = 'Continue'

try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

function Exit-With([int]$Code, [string]$Message) {
    if ($Json) {
        $obj = @{ status = $Code; message = $Message }
        Write-Output ($obj | ConvertTo-Json -Compress)
    }
    else {
        Write-Host $Message
    }
    exit $Code
}

$repoRoot = & git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or -not $repoRoot) { Exit-With 1 '当前目录不在 git 仓库里。' }
$rootPath = ($repoRoot | Out-String).Trim()
if (-not $rootPath) { Exit-With 1 'git rev-parse --show-toplevel 返回空。' }
try { Set-Location -LiteralPath $rootPath -ErrorAction Stop } catch { Exit-With 1 ('无法进入仓库根目录: ' + $rootPath + ' :: ' + $_.Exception.Message) }

$remotes = @(& git remote)
if ($remotes -notcontains $UpstreamRemote) { Exit-With 1 ("找不到远端 " + $UpstreamRemote + "。") }

& git fetch $UpstreamRemote --tags --prune --quiet 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Exit-With 1 'fetch 上游失败（网络或凭据问题）。' }

$localCursor = ''
if (Test-Path $CursorFile) {
    $localCursor = (Get-Content $CursorFile -Raw | Out-String).Trim()
}

$remoteTag = (& git describe --tags --abbrev=0 "$UpstreamRemote/main" 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { Exit-With 1 '无法从 upstream/main 解析 tag。' }

$remoteSha = (& git rev-parse --short "$UpstreamRemote/main^{commit}" | Out-String).Trim()

function ConvertTo-VersionNumber([string]$Tag) {
    if ($Tag -match '^v?(\d+(\.\d+)*)$') {
        $parts = $Matches[1].Split('.')
        $nums = @()
        foreach ($p in $parts) { $nums += [int]$p }
        while ($nums.Count -lt 4) { $nums += 0 }
        return [version]($nums -join '.')
    }
    return $null
}

$localVersion = ConvertTo-VersionNumber $localCursor
$remoteVersion = ConvertTo-VersionNumber $remoteTag

$behind = $false
if (-not $localVersion -or -not $remoteVersion) {
    $behind = ($localCursor -ne $remoteTag)
}
else {
    $behind = ($remoteVersion -gt $localVersion)
}

$message = 'local=' + $(if ($localCursor) { $localCursor } else { '(none)' }) + ' upstream=' + $remoteTag + ' (' + $remoteSha + ')'

if ($behind) {
    if ($Json) {
        $obj = @{ status = 10; behind = $true; local = $localCursor; upstream = $remoteTag; sha = $remoteSha; message = $message }
        Write-Output ($obj | ConvertTo-Json -Compress)
        exit 10
    }
    Write-Host ('上游有新快照: ' + $remoteTag + ' (' + $remoteSha + ')，本地游标 ' + $(if ($localCursor) { $localCursor } else { '(空)' })) -ForegroundColor Yellow
    Write-Host '下一步: powershell -NoProfile -File scripts/sync-upstream.ps1 -DryRun'
    exit 10
}

Exit-With 0 ('已是最新: ' + $message)
