#Requires -Version 5.1
<#
.SYNOPSIS
    把上游 glassdesk-open-source 的最新源码快照合并到本地产品主线（供人和 agent 共用）。

.DESCRIPTION
    上游只发布"压缩快照"（每个 commit 是一个完整版本），因此升级策略是整版 merge，
    不是 rebase、也不是 cherry-pick。

    脚本做机械动作：校验 remote -> fetch -> 展示 diff -> 建 sync 分支 -> merge -> 构建 -> 测试。
    冲突时会把"冲突文件 + 上游对这些文件的改动"导出成报告，方便 agent 逐条决策：
        artifacts/fork-sync/<时间戳>/REPORT.md
        artifacts/fork-sync/<时间戳>/upstream-<文件名>.diff

    退出码（agent 按码分支）：
        0  同步完成且构建+测试通过
        1  前置检查失败（remote / 工作区 / 分支名冲突）
        2  存在未解决冲突，需人工或 agent 继续处理（--Resume 续跑）
        3  合并成功但构建或测试失败
        4  参数错误

.PARAMETER TargetTag
    目标版本：tag 名（如 v1.4.0）或 upstream 分支名。默认取最新 tag（回退 upstream/main）。

.PARAMETER DryRun
    只展示将要发生的变更，不创建分支、不合并、不跑测试。

.PARAMETER Resume
    续跑：已解完冲突（或已验证过），跳过 fetch/合并，只重跑构建+测试并刷新报告。

.PARAMETER SkipTests
    跳过构建与测试。只在明确知道后果时使用。

.PARAMETER NoMirrorUpdate
    不更新本地 upstream-mirror 分支。

.PARAMETER SyncBranchPrefix
    同步分支名前缀，默认 sync/。

.PARAMETER UpstreamRemote
    上游远端名，默认 upstream。

.PARAMETER CursorFile
    同步游标文件，默认 docs/fork/LAST-SYNCED。

.EXAMPLE
    powershell -NoProfile -File scripts/sync-upstream.ps1 -DryRun

.EXAMPLE
    powershell -NoProfile -File scripts/sync-upstream.ps1 -TargetTag v1.4.0

.EXAMPLE
    # 解完冲突后
    powershell -NoProfile -File scripts/sync-upstream.ps1 -Resume

.NOTES
    规则来源：AGENTS.md 第 7 节、docs/fork/FORK-GUIDE.md 第 7 节
    agent 执行协议：docs/fork/AGENT-SYNC-PROTOCOL.md
#>
[CmdletBinding()]
param(
    [string]$TargetTag,
    [switch]$DryRun,
    [switch]$Resume,
    [switch]$SkipTests,
    [switch]$NoMirrorUpdate,
    [string]$SyncBranchPrefix = 'sync/',
    [string]$UpstreamRemote = 'upstream',
    [string]$CursorFile = 'docs/fork/LAST-SYNCED'
)

# 注意：保持 Continue。PS 5.1 下 native 命令写 stderr 会被 Stop 变成终止错误，
# 我们要靠 $LASTEXITCODE 判断 git/dotnet 的成败。
$ErrorActionPreference = 'Continue'

try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

$script:ReportDir = $null

# ---------------------------------------------------------------- 输出助手

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "> $Message" -ForegroundColor Cyan
}

function Write-Info([string]$Message) {
    Write-Host "  $Message" -ForegroundColor DarkGray
}

function Write-Warn([string]$Message) {
    Write-Host "  ! $Message" -ForegroundColor Yellow
}

function Exit-With([int]$Code, [string]$Message) {
    Write-Host ''
    if ($Code -eq 0) { Write-Host "OK $Message" -ForegroundColor Green }
    else { Write-Host "FAIL($Code) $Message" -ForegroundColor Red }
    exit $Code
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $output = & git @GitArgs 2>&1
    return @{ Code = $LASTEXITCODE; Output = ($output | Out-String).Trim() }
}

function Test-GitRef([string]$Ref) {
    & git rev-parse --verify --quiet "$Ref^{commit}" 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Get-GitSha([string]$Ref) {
    $sha = & git rev-parse --short "$Ref^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) { return '?' }
    return ($sha | Out-String).Trim()
}

# ---------------------------------------------------------------- 报告

function New-ReportDir {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path (Join-Path 'artifacts\fork-sync' $stamp) ''
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $script:ReportDir = $dir
    return $dir
}

function Add-Report([string]$Text) {
    if (-not $script:ReportDir) { return }
    Add-Content -Path (Join-Path $script:ReportDir 'REPORT.md') -Value $Text -Encoding UTF8
}

# 导出与冲突文件相关的上游改动，agent 靠它判断"上游为什么这么改"
function Export-ConflictEvidence {
    param(
        [string]$BaseRef,
        [string]$TargetRef,
        [string[]]$Files
    )
    if (-not $script:ReportDir) { return }
    $index = 0
    foreach ($f in $Files) {
        $index++
        $safe = ($f -replace '[\\/:*?"<>|]', '_')
        $out = Join-Path $script:ReportDir ("upstream-{0:D2}-{1}.diff" -f $index, $safe)
        if ($BaseRef) {
            & git diff "$BaseRef..$TargetRef" -- $f > $out 2>&1
        }
        else {
            & git diff "main...$TargetRef" -- $f > $out 2>&1
        }
        Add-Report ("- 上游改动: ``upstream-{0:D2}-{1}.diff`` (文件: ``{2}``)" -f $index, $safe, $f)
    }
}

# ---------------------------------------------------------------- 前置检查

$repoRoot = & git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or -not $repoRoot) { Exit-With 1 '当前目录不在 git 仓库里。' }
$rootPath = ($repoRoot | Out-String).Trim()
if (-not $rootPath) { Exit-With 1 'git rev-parse --show-toplevel 返回空。' }
try { Set-Location -LiteralPath $rootPath -ErrorAction Stop } catch { Exit-With 1 ('无法进入仓库根目录: ' + $rootPath + ' :: ' + $_.Exception.Message) }
Write-Info ("仓库根目录: " + (Get-Location).Path)

Write-Step '检查 remote'

$remotes = @(& git remote)
if ($remotes -notcontains $UpstreamRemote) {
    Exit-With 1 ("找不到远端 " + $UpstreamRemote + "。先执行 docs/fork/FORK-GUIDE.md 第 3 节的 remote 迁移。")
}

$upstreamUrl = (& git remote get-url $UpstreamRemote | Out-String).Trim()
Write-Info ("$UpstreamRemote = " + $upstreamUrl)
if ($upstreamUrl -notmatch 'glassdesk-open-source') {
    Write-Warn '上游远端地址不含 glassdesk-open-source，确认无误再继续。'
}

$pushUrl = ''
& git remote get-url --push $UpstreamRemote 2>$null | ForEach-Object { $pushUrl = $_ }
if ($pushUrl -match 'github\.com') {
    Write-Warn ("上游远端仍可推(" + $pushUrl + ")，建议: git remote set-url --push $UpstreamRemote DISABLED")
}

# ---------------------------------------------------------------- Resume 分支

if ($Resume) {
    Write-Step 'Resume 模式：跳过 fetch/合并，直接验证当前分支'
    if (-not (Test-Path '.git\MERGE_HEAD')) {
        Write-Warn '当前不在合并状态；将对当前 HEAD 跑构建与测试。'
    }

    # 收尾提示依赖这几个变量，Resume 时必须从当前状态反推
    $syncBranch = ((& git rev-parse --abbrev-ref HEAD | Out-String).Trim())
    $targetRef = 'HEAD'
    $foundTag = (& git describe --tags --abbrev=0 HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $foundTag) { $targetName = $foundTag } else { $targetName = (Get-GitSha 'HEAD') }
    $baseRef = $null

    $dir = New-ReportDir
    Add-Report '# 上游同步报告（Resume）'
    Add-Report ''
    Add-Report ('- 时间: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    Add-Report ('- 当前 HEAD: ' + (Get-GitSha 'HEAD'))
    Add-Report ('- 当前分支: ' + $syncBranch)
    Add-Report ('- 目标版本(推断): ' + $targetName)
    Add-Report ''
    $skipMerge = $true
}
else {
    $skipMerge = $false
}

# ---------------------------------------------------------------- 工作区检查

if (-not $skipMerge) {
    if (Test-Path '.git\MERGE_HEAD') {
        Exit-With 2 '已处于合并中（有未完成冲突）。解完冲突后跑 -Resume，或先 git merge --abort。'
    }

    $dirty = (& git status --porcelain | Out-String).Trim()
    if ($dirty) {
        Exit-With 1 ("工作区不干净，先提交或 stash：`n" + $dirty)
    }

    Write-Step '拉取上游 tag 与分支'
    $fetch = Invoke-Git fetch $UpstreamRemote --tags --prune
    if ($fetch.Code -ne 0) { Exit-With 1 ("fetch 失败: " + $fetch.Output) }
    Write-Info 'fetch 完成'

    # 目标版本
    if ([string]::IsNullOrWhiteSpace($TargetTag)) {
        $targetRef = "$UpstreamRemote/main"
        $latestTag = (& git describe --tags --abbrev=0 $targetRef 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $latestTag) { $targetName = $latestTag }
        else { $targetName = Get-GitSha $targetRef }
    }
    else {
        $targetRef = $null
        foreach ($candidate in @($TargetTag, "refs/tags/$TargetTag", "$UpstreamRemote/$TargetTag")) {
            if (Test-GitRef $candidate) { $targetRef = $candidate; break }
        }
        if (-not $targetRef) { Exit-With 1 ("找不到目标版本 " + $TargetTag + "，先 git fetch $UpstreamRemote --tags。") }
        $targetName = $TargetTag
    }

    Write-Step ('目标版本: ' + $targetName + ' (' + (Get-GitSha $targetRef) + ')')

    # 比较基准
    $baseRef = $null
    if (Test-Path $CursorFile) {
        $cursor = (Get-Content $CursorFile -Raw | Out-String).Trim()
        if ($cursor -and (Test-GitRef $cursor)) { $baseRef = $cursor }
    }
    if (-not $baseRef) {
        $mergeBase = (& git merge-base main $targetRef 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $mergeBase) {
            $baseRef = $mergeBase
            Write-Warn ('未找到可用的 ' + $CursorFile + '，用 merge-base 作基准 (' + (Get-GitSha $baseRef) + ')。')
        }
        else {
            Write-Warn ('没有可用的比较基准，直接对比 main 与 ' + $targetName + '。')
        }
    }

    Write-Step '上游这个版本改了什么（压缩快照下 diff 就是 release note）'
    & git log --oneline $targetRef -5
    Write-Host ''
    if ($baseRef) {
        Write-Info ('对比区间: ' + (Get-GitSha $baseRef) + '..' + $targetName)
        & git diff --stat "$baseRef..$targetRef"
    }
    else {
        & git diff --stat "main...$targetRef"
    }
    Write-Host ''
    Write-Info ('重点细看: git diff ' + $baseRef + '..' + $targetRef + ' -- src/GlassDesk.UI src/GlassDesk.Dock src/GlassDesk.Launcher')

    if ($DryRun) {
        Write-Host ''
        Write-Host 'DryRun: 未创建分支、未合并。' -ForegroundColor Green
        exit 0
    }

    # 同步分支
    $syncBranch = "$SyncBranchPrefix$targetName"
    $existing = (& git branch --list $syncBranch | Out-String).Trim()
    if ($existing) {
        Exit-With 1 ("分支 " + $syncBranch + " 已存在。处理: git log --oneline main.." + $syncBranch + " 或 git branch -D " + $syncBranch)
    }

    Write-Step ("创建同步分支 " + $syncBranch + '（基于 main）')
    $co = Invoke-Git checkout -b $syncBranch main
    if ($co.Code -ne 0) { Exit-With 1 ("checkout 失败: " + $co.Output) }

    Write-Step ('合并 ' + $targetRef)
    $merge = Invoke-Git merge --no-ff --no-edit $targetRef
    if ($merge.Code -ne 0) {
        Write-Host $merge.Output

        $dir = New-ReportDir
        Add-Report '# 上游同步报告（存在冲突）'
        Add-Report ''
        Add-Report ('- 时间: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
        Add-Report ('- 同步分支: ' + $syncBranch)
        Add-Report ('- 基准: ' + $(if ($baseRef) { $baseRef } else { '(merge-base)' }))
        Add-Report ('- 目标: ' + $targetRef + ' (' + $targetName + ')')
        Add-Report ''
        Add-Report '## 冲突文件'
        $conflicts = @(& git diff --name-only --diff-filter=U | Where-Object { $_ })
        foreach ($c in $conflicts) { Add-Report ('- ``' + $c + '``') }
        Add-Report ''
        Add-Report '## 上游对冲突文件的改动（决策依据）'
        Export-ConflictEvidence -BaseRef $baseRef -TargetRef $targetRef -Files $conflicts
        Add-Report ''
        Add-Report '## 处理顺序建议'
        Add-Report '1. 低冲突面文件（新增文件、Settings、AppServices）先解'
        Add-Report '2. 每解一个文件立刻 dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release'
        Add-Report '3. 全部 add 并 commit 后执行: sync-upstream.ps1 -Resume'
        Add-Report ''
        Add-Report '规则见 docs/fork/AGENT-SYNC-PROTOCOL.md'

        Write-Host ''
        Write-Host ('冲突报告: ' + $dir + 'REPORT.md') -ForegroundColor Yellow
        Exit-With 2 ('未解决的冲突 ' + $conflicts.Count + ' 个，报告已导出。')
    }

    Write-Info '合并无冲突'
    $dir = New-ReportDir
    Add-Report '# 上游同步报告'
    Add-Report ''
    Add-Report ('- 时间: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    Add-Report ('- 同步分支: ' + $syncBranch)
    Add-Report ('- 基准: ' + $(if ($baseRef) { $baseRef } else { '(merge-base)' }))
    Add-Report ('- 目标: ' + $targetRef + ' (' + $targetName + ')')
    Add-Report '- 冲突: 无'
    Add-Report ''
}

# ---------------------------------------------------------------- 验证

$verdict = 'pass'

# 基线：上游公开快照本身就有已知失败（缺 build\package.ps1）。
# 验证门槛 = "不引入新失败"，不是"全绿"。
$baseline = $null
if (Test-Path 'docs\fork\BASELINE.json') {
    try { $baseline = (Get-Content 'docs\fork\BASELINE.json' -Raw | Out-String) | ConvertFrom-Json }
    catch { Write-Warn 'BASELINE.json 解析失败，按"任何失败都算回归"处理。' }
}
else {
    Write-Warn '没有 docs\fork\BASELINE.json，按"任何失败都算回归"处理。'
}

function Get-KnownFailures([string]$Project, $Baseline) {
    if (-not $Baseline) { return @() }
    $entry = $null
    if ($Baseline.projects) {
        $prop = $Baseline.projects.PSObject.Properties[$Project]
        if ($prop) { $entry = $prop.Value }
    }
    if (-not $entry -or -not $entry.knownFailures) { return @() }
    return @($entry.knownFailures)
}

# 用户级 SDK 安装时 dotnet 不在 PATH，且 apphost 找不到运行时会报
# "You must install .NET"。统一在这里解析并注入环境。
function Resolve-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    $candidates += 'C:\Program Files\dotnet\dotnet.exe'
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

# 跑外部命令并把退出码显式取出来（CommandNotFound 不会更新 LASTEXITCODE，必须自己复位）
function Invoke-Tool {
    param(
        [string]$Exe,
        [string[]]$ToolArgs,
        [string]$LogPath
    )
    $global:LASTEXITCODE = 0
    if ($LogPath) {
        & $Exe @ToolArgs > $LogPath 2>&1
    }
    else {
        & $Exe @ToolArgs
    }
    return $LASTEXITCODE
}

if (-not $SkipTests) {
    $dotnetExe = Resolve-Dotnet
    if (-not $dotnetExe) {
        Exit-With 1 '找不到 dotnet.exe。安装 .NET 10 SDK（>= 10.0.401）后重试，见 docs/fork/BASELINE.md。'
    }
    $env:DOTNET_ROOT = Split-Path $dotnetExe
    $env:PATH = (Split-Path $dotnetExe) + ';' + $env:PATH
    Write-Info ('dotnet: ' + $dotnetExe)

    Write-Step '构建 GlassDesk.App（不要用 slnx，整解会 NU1201）'
    $buildLog = 'artifacts\fork-sync\last-build.log'
    New-Item -ItemType Directory -Force -Path 'artifacts\fork-sync' | Out-Null
    $buildCode = Invoke-Tool $dotnetExe @('build', 'src/GlassDesk.App/GlassDesk.App.csproj', '-c', 'Release', '--nologo') $buildLog
    if ($buildCode -ne 0) {
        Add-Report '## 构建: 失败'
        Add-Report ('日志: ``' + $buildLog + '``')
        Get-Content $buildLog -Tail 40 | ForEach-Object { Add-Report ('    ' + $_) }
        Exit-With 3 ('构建失败 (exit=' + $buildCode + ')，见 ' + $buildLog)
    }
    Add-Report '## 构建: 通过'

    foreach ($project in @(
            'tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj',
            'tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj',
            'tests/GlassDesk.Dock.Tests/GlassDesk.Dock.Tests.csproj')) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
        Write-Step ('测试 ' + $name)
        $trx = 'artifacts\testresults\fork-sync-' + $name + '.trx'
        New-Item -ItemType Directory -Force -Path 'artifacts\testresults' | Out-Null
        $log = 'artifacts\fork-sync\' + $name + '.log'
        Remove-Item $trx -ErrorAction SilentlyContinue
        $code = Invoke-Tool $dotnetExe @('test', $project, '-c', 'Release', '--nologo',
            '--logger', ('trx;LogFileName=' + [System.IO.Path]::GetFileName($trx)),
            '--results-directory', 'artifacts\testresults') $log
        $counts = 'n/a'
        $newFailures = @()
        $knownFailures = Get-KnownFailures $name $baseline
        if (Test-Path $trx) {
            try {
                $doc = [xml](Get-Content $trx)
                $c = $doc.TestRun.ResultSummary.Counters
                $counts = 'total=' + $c.total + ' passed=' + $c.passed + ' failed=' + $c.failed
                $failedNames = @($doc.TestRun.Results.UnitTestResult |
                        Where-Object { $_.outcome -eq 'Failed' } |
                        ForEach-Object { $_.testName } |
                        Sort-Object -Unique)
                $newFailures = @($failedNames | Where-Object { $knownFailures -notcontains $_ })

                # 抖动的用例（DPI/时序/桌面截屏）重跑一次再定性，避免误报阻塞同步
                if ($newFailures.Count -gt 0) {
                    Write-Warn ('  发现 ' + $newFailures.Count + ' 个基线外失败，重跑一次确认是否抖动')
                    Remove-Item $trx -ErrorAction SilentlyContinue
                    $retryCode = Invoke-Tool $dotnetExe @('test', $project, '-c', 'Release', '--nologo',
                        '--logger', ('trx;LogFileName=' + [System.IO.Path]::GetFileName($trx)),
                        '--results-directory', 'artifacts\testresults') $log
                    if (Test-Path $trx) {
                        $doc2 = [xml](Get-Content $trx)
                        $c2 = $doc2.TestRun.ResultSummary.Counters
                        $counts = 'total=' + $c2.total + ' passed=' + $c2.passed + ' failed=' + $c2.failed
                        $failedNames2 = @($doc2.TestRun.Results.UnitTestResult |
                                Where-Object { $_.outcome -eq 'Failed' } |
                                ForEach-Object { $_.testName } |
                                Sort-Object -Unique)
                        $newFailures2 = @($failedNames2 | Where-Object { $knownFailures -notcontains $_ })
                        if ($newFailures2.Count -eq 0) {
                            Write-Info ('  重跑通过 → 判定为抖动，不计失败: ' + ($newFailures -join ', '))
                            Add-Report ('## 抖动（重跑通过，未计入失败）: ' + $name)
                            foreach ($nf in $newFailures) { Add-Report ('- ``' + $nf + '``') }
                            $newFailures = @()
                        }
                        else {
                            $newFailures = $newFailures2
                        }
                    }
                }
            }
            catch { $counts = 'trx 解析失败'; $newFailures = @('<trx>') }
        }
        else {
            $newFailures = @('<测试没有产出 TRX：命令本身失败（dotnet 缺失 / 参数错误 / 崩溃），日志见 ' + $log + '>')
        }

        if ($newFailures.Count -eq 0) {
            $label = if ($code -eq 0) { '通过' } else { '通过（仅基线已知失败）' }
            Write-Info ($name + ' ' + $label + ' (' + $counts + ', 已知失败 ' + $knownFailures.Count + ')')
            Add-Report ('## 测试 ' + $name + ': ' + $label + ' (' + $counts + ')')
        }
        else {
            Write-Warn ($name + ' 出现 ' + $newFailures.Count + ' 个新失败 (' + $counts + ')，日志 ' + $log)
            Add-Report ('## 测试 ' + $name + ': 新失败 ' + $newFailures.Count + ' 个 (' + $counts + ')')
            Add-Report ('日志: ``' + $log + '``')
            Add-Report ''
            Add-Report '新失败用例（对照基线新增，必须处理）：'
            foreach ($nf in $newFailures) { Add-Report ('- ``' + $nf + '``') }
            Write-Host ''
            Write-Host '新失败用例（基线里没有）：' -ForegroundColor Yellow
            foreach ($nf in $newFailures) { Write-Host ('  - ' + $nf) -ForegroundColor Yellow }
            $verdict = 'fail'
        }
    }
}

# ---------------------------------------------------------------- 收尾

Write-Host ''
if ($verdict -ne 'pass') {
    Exit-With 3 '合并成功但出现基线外的新失败。先看 artifacts/fork-sync 报告里的新失败用例：是冲突解法错，还是上游改了行为导致测试断言过期。'
}

$currentSync = (& git rev-parse --abbrev-ref HEAD | Out-String).Trim()
if (-not $NoMirrorUpdate -and -not $Resume) {
    Write-Step '更新本地镜像分支 upstream-mirror'
    $mb = Invoke-Git branch -f upstream-mirror "$UpstreamRemote/main"
    if ($mb.Code -eq 0) { Write-Info 'upstream-mirror 已对齐 upstream/main' }
    else { Write-Warn ('镜像分支更新失败: ' + $mb.Output) }
    $currentSync = (& git rev-parse --abbrev-ref HEAD | Out-String).Trim()
}

if ($script:ReportDir) {
    Write-Host ('报告: ' + $script:ReportDir + 'REPORT.md') -ForegroundColor Cyan
}

Write-Host ''
Write-Host ('OK 同步分支就绪: ' + $currentSync) -ForegroundColor Green
Write-Host ''
Write-Host '人工 / agent 收尾步骤:' -ForegroundColor Cyan
Write-Host '  1) 手验关键路径: Dock 玻璃重建、启动台分页、设置项、托盘、开机自启'
Write-Host '  2) 确认自有功能仍在（对照 docs/fork/FEATURES.md，若存在）'
Write-Host '  3) 合并回主线: git checkout main ; git merge --no-ff ' + $currentSync
Write-Host ('  4) 更新游标: "' + $targetName + '" > ' + $CursorFile + '  然后提交')
Write-Host ('  5) 删除同步分支: git branch -d ' + $currentSync)
exit 0
