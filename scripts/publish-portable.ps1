#Requires -Version 5.1
<#
.SYNOPSIS
    从源码生成便携版（self-contained）：一条命令产出可直接运行的 GlassDesk.exe 分发包。

.DESCRIPTION
    等价于 README 的"从源码生成便携版"流程，另外处理了三个真实会踩的坑：

      1. dotnet 解析：PATH 里默认命中的 C:\Program Files\dotnet 往往只有运行时，
         没有 sdk 目录，直接 dotnet publish 会报 "No .NET SDKs were found"。
         本脚本只接受 --list-sdks 非空的 muxer。
      2. 文件占用：正在运行的 GlassDesk 锁住 bin\* 与 publish 输出，会导致
         MSB3021。脚本先停掉本仓库路径下的 GlassDesk 进程。
      3. 分发包卫生：确保输出目录里没有 Data/、settings.json、apps.json
         （上游 README 明确要求不要把用户 Data 放进分发包）。

.PARAMETER OutputDir
    输出目录，默认 artifacts\publish。必须在仓库的 artifacts\ 下（安全护栏）。

.PARAMETER Zip
    额外生成 artifacts\GlassDesk-<version>-win-x64-portable.zip。

.PARAMETER Run
    发布完成后立即启动 GlassDesk.exe。

.PARAMETER KeepPrevious
    不清理输出目录（默认先清空，避免旧文件残留）。

.PARAMETER Version
    覆盖版本号（默认取 Directory.Build.props 的 Version）。

.EXAMPLE
    powershell -NoProfile -File scripts/publish-portable.ps1

.EXAMPLE
    powershell -NoProfile -File scripts/publish-portable.ps1 -Run -Zip

.NOTES
    规则来源：AGENTS.md 第 6 节。本脚本属于 fork 自有文件，不改上游 build\publish.ps1。
#>
[CmdletBinding()]
param(
    [string]$OutputDir = 'artifacts\publish',
    [switch]$Zip,
    [switch]$Run,
    [switch]$KeepPrevious,
    [string]$Version
)

$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

$started = Get-Date

function Write-Step([string]$m) { Write-Host ''; Write-Host "> $m" -ForegroundColor Cyan }
function Write-Info([string]$m) { Write-Host "  $m" -ForegroundColor DarkGray }
function Write-Warn2([string]$m) { Write-Host "  ! $m" -ForegroundColor Yellow }
function Exit-With([int]$code, [string]$m) {
    Write-Host ''
    if ($code -eq 0) { Write-Host "OK $m" -ForegroundColor Green }
    else { Write-Host "FAIL($code) $m" -ForegroundColor Red }
    exit $code
}

# ---------------------------------------------------------------- 仓库与 dotnet

$repoRoot = & git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or -not $repoRoot) { Exit-With 1 '当前目录不在 git 仓库里。' }
$repoRoot = ($repoRoot | Out-String).Trim()
# git 在 Windows 上可能返回正斜杠（D:/a/b），而进程 Path 是反斜杠，统一归一化后再比较
$repoRoot = $repoRoot -replace '/', '\'
try { Set-Location -LiteralPath $repoRoot -ErrorAction Stop } catch { Exit-With 1 ('无法进入仓库根目录: ' + $repoRoot) }
Write-Info ('仓库: ' + $repoRoot)

function Resolve-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    $candidates += 'C:\Program Files\dotnet\dotnet.exe'
    foreach ($c in $candidates) {
        if (-not $c -or -not (Test-Path $c)) { continue }
        $sdks = & $c --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) { return $c }
    }
    return $null
}

Write-Step '解析 .NET SDK'
$dotnet = Resolve-Dotnet
if (-not $dotnet) {
    Exit-With 1 @'
找不到带 SDK 的 dotnet.exe（只找到运行时的 muxer 不算）。
修复任选其一：
  1) 本会话临时前置： $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
  2) 永久前置用户级 PATH：
     [Environment]::SetEnvironmentVariable('PATH', "$env:LOCALAPPDATA\Microsoft\dotnet;" + [Environment]::GetEnvironmentVariable('PATH','User'), 'User')
  3) 安装系统级 SDK： winget install Microsoft.DotNet.SDK.10
'@
}
$env:DOTNET_ROOT = Split-Path $dotnet
$env:PATH = (Split-Path $dotnet) + ';' + $env:PATH
$sdkVersion = (& $dotnet --version | Out-String).Trim()
Write-Info ('dotnet: ' + $dotnet)
Write-Info ('SDK: ' + $sdkVersion)

# ---------------------------------------------------------------- 版本号

if (-not $Version) {
    $props = [xml](Get-Content 'Directory.Build.props' -Raw)
    $Version = @($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
}
if (-not $Version) { Exit-With 1 '读不到版本号（Directory.Build.props 的 Version 为空）。' }
Write-Info ('版本: ' + $Version)

# ---------------------------------------------------------------- 输出目录护栏

$outFull = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
$allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not $outFull.StartsWith($allowedRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    Exit-With 1 ('输出目录必须在仓库 artifacts\ 下: ' + $outFull)
}
Write-Info ('输出目录: ' + $outFull)

# ---------------------------------------------------------------- 停掉占用的进程

Write-Step '检查并停止占用输出目录的 GlassDesk 进程'
$stopped = @()
foreach ($proc in @(Get-Process GlassDesk -ErrorAction SilentlyContinue)) {
    $path = $null
    try { $path = $proc.Path } catch { $path = $null }
    if ($path -and $path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $stopped += ('pid=' + $proc.Id + ' ' + $path)
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    elseif (-not $path) {
        $stopped += ('pid=' + $proc.Id + ' (路径不可读，已停)')
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}
if ($stopped.Count -gt 0) {
    foreach ($s in $stopped) { Write-Warn2 ('已停止 ' + $s) }
    Start-Sleep -Milliseconds 1200
}
else {
    Write-Info '没有需要停止的进程'
}

# ---------------------------------------------------------------- 清理输出

if (-not $KeepPrevious -and (Test-Path $outFull)) {
    Write-Step '清理旧输出'
    Remove-Item $outFull -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $outFull) {
        # 逐文件删一遍，把真正被占用的文件点名（常见：运行时生成的 .glassdesk-data-*.lease）
        $locked = @()
        foreach ($item in @(Get-ChildItem $outFull -Recurse -Force)) {
            try { Remove-Item $item.FullName -Recurse -Force -ErrorAction Stop }
            catch { $locked += ($item.FullName + ' :: ' + $_.Exception.Message) }
        }
        if (Test-Path $outFull) {
            Write-Warn2 '以下文件无法删除：'
            $locked | Select-Object -First 5 | ForEach-Object { Write-Host ('    ' + $_) -ForegroundColor Yellow }
            Exit-With 1 ('清理失败: ' + $outFull + '；先手动退出 GlassDesk（含 artifacts\\publish 下运行的实例）再重试。')
        }
    }
    Write-Info '已清空'
}
New-Item -ItemType Directory -Force -Path $outFull | Out-Null

# ---------------------------------------------------------------- 发布

Write-Step 'dotnet publish（self-contained + ReadyToRun，约 1-3 分钟）'
$logDir = Join-Path $repoRoot 'artifacts\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ('publish-portable-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')

$publishArgs = @(
    'publish', 'src/GlassDesk.App/GlassDesk.App.csproj',
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishReadyToRun=true',
    ('-p:InformationalVersion=' + $Version),
    '-p:IncludeSourceRevisionInInformationalVersion=false',
    '-o', $outFull
)
$global:LASTEXITCODE = 0
& $dotnet @publishArgs > $log 2>&1
$publishCode = $LASTEXITCODE
if ($publishCode -ne 0) {
    Get-Content $log -Tail 25 -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    Exit-With 2 ('publish 失败 (exit=' + $publishCode + ')，完整日志: ' + $log)
}
Write-Info ('publish 完成，日志: ' + $log)

# ---------------------------------------------------------------- 随包许可文件

Write-Step '复制许可与第三方声明'
$missing = @()
foreach ($required in @('LICENSE', 'NOTICE', 'docs\third-party')) {
    if (-not (Test-Path $required)) { $missing += $required }
}
if ($missing.Count -gt 0) { Exit-With 1 ('缺少必带文件: ' + ($missing -join ', ')) }

Copy-Item 'LICENSE' (Join-Path $outFull 'LICENSE.txt') -Force
Copy-Item 'NOTICE' (Join-Path $outFull 'NOTICE.txt') -Force
Copy-Item 'docs\third-party' (Join-Path $outFull 'third-party') -Recurse -Force
Write-Info 'LICENSE.txt / NOTICE.txt / third-party 已就位'

# ---------------------------------------------------------------- 分发包卫生

Write-Step '检查分发包卫生（不得含用户数据）'
$forbidden = @('Data', 'settings.json', 'apps.json', 'dock-layout.json', 'launcher-layout.json')
$found = @()
foreach ($f in $forbidden) {
    $p = Join-Path $outFull $f
    if (Test-Path $p) { $found += $f; Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
}
if ($found.Count -gt 0) { Write-Warn2 ('已从输出中移除: ' + ($found -join ', ')) }
else { Write-Info '无 Data/配置文件残留' }

# ---------------------------------------------------------------- 结果校验

Write-Step '校验产物'
$exe = Join-Path $outFull 'GlassDesk.exe'
if (-not (Test-Path $exe)) { Exit-With 3 '缺少 GlassDesk.exe。' }
foreach ($runtime in @('coreclr.dll', 'hostfxr.dll')) {
    if (-not (Test-Path (Join-Path $outFull $runtime))) { Exit-With 3 ('不是 self-contained 产物，缺 ' + $runtime) }
}

$fi = Get-Item $exe
$allFiles = Get-ChildItem $outFull -Recurse -File
$totalMB = [math]::Round((($allFiles | Measure-Object Length -Sum).Sum) / 1MB, 1)
$vi = $fi.VersionInfo
Write-Info ('GlassDesk.exe: ' + [math]::Round($fi.Length / 1MB, 2) + 'MB, fileVersion=' + $vi.FileVersion + ', productVersion=' + $vi.ProductVersion)
Write-Info ('共 ' + $allFiles.Count + ' 个文件, ' + $totalMB + 'MB')

# ---------------------------------------------------------------- 可选 zip

$zipPath = $null
if ($Zip) {
    Write-Step '打包 zip'
    $zipPath = Join-Path (Join-Path $repoRoot 'artifacts') ('GlassDesk-' + $Version + '-win-x64-portable.zip')
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $outFull '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Info ('zip: ' + $zipPath + ' (' + $zipMB + 'MB)')
}

# ---------------------------------------------------------------- 可选运行

if ($Run) {
    Write-Step '启动便携版'
    Get-Process GlassDesk -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $p = Start-Process -FilePath $exe -WorkingDirectory $outFull -PassThru
    Start-Sleep -Seconds 6
    $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    if ($alive) {
        Write-Info ('运行中 pid=' + $alive.Id + ' 内存=' + [math]::Round($alive.WorkingSet64 / 1MB) + 'MB')
    }
    else {
        Exit-With 3 ('启动后立即退出 (exit=' + $p.ExitCode + ')，看 ' + (Join-Path $outFull 'Data\logs\startup.log'))
    }
}

$elapsed = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
Write-Host ''
Write-Host ('便携版: ' + $exe) -ForegroundColor Green
if ($zipPath) { Write-Host ('压缩包: ' + $zipPath) -ForegroundColor Green }
Write-Host ('耗时: ' + $elapsed + 's') -ForegroundColor DarkGray
Write-Host '分发时只拷这个目录；不要包含 Data/（运行时才会在 exe 同目录生成）。' -ForegroundColor DarkGray
exit 0
