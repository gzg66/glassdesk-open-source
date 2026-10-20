# fork-verify.ps1 -- post-sync self-check: Release build (+ optional baseline tests).
#
# Called by scripts/fork-verify.sh (WSL / Git Bash). Can also run directly in PowerShell.
# NOTE: keep this file pure ASCII. Windows PowerShell 5.1 decodes BOM-less UTF-8 as ANSI,
#       so any non-ASCII byte can corrupt parsing (that is why the doc block is English).
#
#   powershell -NoProfile -File scripts\fork-verify.ps1           # build App
#   powershell -NoProfile -File scripts\fork-verify.ps1 -Kill     # kill GlassDesk first
#   powershell -NoProfile -File scripts\fork-verify.ps1 -Full     # build + gating tests
#
# Exit codes: 0 ok, 1 build failed, 2 environment problem (no SDK), 3 test failed

[CmdletBinding()]
param(
    [switch]$Kill,
    [switch]$Full
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDir
Set-Location $repoRoot

function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidate = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

$dotnet = Resolve-Dotnet
if (-not $dotnet) {
    Write-Host 'FAIL: no .NET SDK found (PATH and %LOCALAPPDATA%\Microsoft\dotnet both empty)' -ForegroundColor Red
    exit 2
}

Write-Host "dotnet: $dotnet"
$sdk = & $dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: dotnet --version failed' -ForegroundColor Red
    exit 2
}
Write-Host "sdk   : $sdk"

if ($Kill) {
    $procs = Get-Process GlassDesk -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force
        Write-Host "killed $($procs.Count) GlassDesk process(es) holding the output directory" -ForegroundColor Yellow
        Start-Sleep -Milliseconds 800
    }
}

Write-Host ''
Write-Host '== build src/GlassDesk.App (Release)'
& $dotnet build (Join-Path $repoRoot 'src\GlassDesk.App\GlassDesk.App.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: build failed' -ForegroundColor Red
    exit 1
}

if (-not $Full) {
    Write-Host 'OK: build passed (use -Full for the baseline test projects)' -ForegroundColor Green
    exit 0
}

$gating = @(
    'tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj',
    'tests\GlassDesk.Dock.Tests\GlassDesk.Dock.Tests.csproj'
)
$informational = @(
    'tests\GlassDesk.AppServices.Tests\GlassDesk.AppServices.Tests.csproj'
)

$failed = 0
foreach ($proj in $gating) {
    Write-Host ''
    Write-Host "== test $proj (gating)"
    & $dotnet test (Join-Path $repoRoot $proj) -c Release --nologo --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { $failed++ }
}
foreach ($proj in $informational) {
    Write-Host ''
    Write-Host "== test $proj (baseline-informational, failures are upstream's)"
    & $dotnet test (Join-Path $repoRoot $proj) -c Release --nologo --logger 'console;verbosity=minimal'
}

if ($failed -gt 0) {
    Write-Host "FAIL: $failed gating test project(s) failed" -ForegroundColor Red
    exit 3
}
Write-Host 'OK: build + gating tests passed' -ForegroundColor Green
exit 0
