# Focused build gate: compiles every project INCLUDING the live auto-trader strategy,
# and runs the behavior tests. Unlike Run-Verification.ps1 this does no doc generation
# and no network smoke test, so it is safe to run repeatedly and on CI.
# NinjaTrader compilation is skipped (with a warning, not a failure) when the NT8
# assemblies are not present, so the pure-.NET gate still runs anywhere.
[CmdletBinding()]
param(
    [switch]$SkipNinjaTrader,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$binRoot = Join-Path $repoRoot "build-out"
New-Item -ItemType Directory -Force -Path $binRoot | Out-Null
$engineDll = Join-Path $binRoot "Intent.Engine.dll"

function Resolve-FirstExisting {
    param([string[]]$Candidates)
    foreach ($c in $Candidates) { if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path } }
    return $null
}

function Invoke-Csc {
    param([string]$Label, [string[]]$Arguments)
    Write-Host "  [csc] $Label" -ForegroundColor DarkGray
    & $csc @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Compile FAILED: $Label" }
}

function Get-Cs {
    param([string]$RelDir, [switch]$Recurse)
    $full = Join-Path $repoRoot $RelDir
    Get-ChildItem -Path $full -Filter *.cs -Recurse:$Recurse | ForEach-Object { $_.FullName }
}

$failures = @()

# 1. Pure engine
try {
    Invoke-Csc "Intent.Engine" (@("/target:library","/nologo","/out:$engineDll") + (Get-Cs "src\Intent.Engine" -Recurse))
} catch { $failures += $_.Exception.Message }

# 2. Console (TCP runner + dashboard)
try {
    Invoke-Csc "Intent.Console" (@("/target:exe","/nologo","/out:$(Join-Path $binRoot 'Intent.StreamRunner.exe')","/r:$engineDll","/r:System.Runtime.Serialization.dll") + (Get-Cs "src\Intent.Console"))
} catch { $failures += $_.Exception.Message }

# 3. Replay
try {
    Invoke-Csc "Intent.Replay" (@("/target:exe","/nologo","/out:$(Join-Path $binRoot 'Intent.Replay.exe')","/r:System.Runtime.Serialization.dll") + (Get-Cs "src\Intent.Replay"))
} catch { $failures += $_.Exception.Message }

# 4. Sweep + backtester
try {
    Invoke-Csc "Intent.Sweep" (@("/target:exe","/nologo","/out:$(Join-Path $binRoot 'Intent.Sweep.exe')","/r:$engineDll","/r:System.Runtime.Serialization.dll") + (Get-Cs "src\Intent.Sweep"))
} catch { $failures += $_.Exception.Message }

# 5. Tests
$testsExe = Join-Path $binRoot "Intent.Engine.Tests.exe"
try {
    Invoke-Csc "Intent.Engine.Tests" (@("/target:exe","/nologo","/out:$testsExe","/r:$engineDll") + (Get-Cs "src\Intent.Engine.Tests"))
} catch { $failures += $_.Exception.Message }

# 6. NinjaTrader assembly INCLUDING the live strategy (the money path)
if (-not $SkipNinjaTrader) {
    $ntBin = "C:\Program Files\NinjaTrader 8\bin"
    $vendor = Resolve-FirstExisting @(
        (Join-Path $env:USERPROFILE "Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Vendor.dll"),
        (Join-Path $env:USERPROFILE "OneDrive\Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Vendor.dll"),
        (Join-Path $ntBin "Custom\Backup\NinjaTrader.Vendor.dll")
    )
    $wpf = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF"
    if ((Test-Path (Join-Path $ntBin "NinjaTrader.Core.dll")) -and $vendor) {
        try {
            Invoke-Csc "IntentLayerV01 + IntentAutoTraderV01 (NinjaTrader)" (@(
                "/define:STANDALONE_VERIFY","/target:library","/nologo",
                "/out:$(Join-Path $binRoot 'IntentLayerV01.dll')",
                "/r:$engineDll",
                "/r:$(Join-Path $ntBin 'NinjaTrader.Core.dll')",
                "/r:$(Join-Path $ntBin 'NinjaTrader.Gui.dll')",
                "/r:$vendor",
                "/r:$(Join-Path $wpf 'WindowsBase.dll')",
                "/r:$(Join-Path $wpf 'PresentationCore.dll')",
                "/r:$(Join-Path $wpf 'PresentationFramework.dll')",
                "/r:System.ComponentModel.DataAnnotations.dll"
            ) + (Get-Cs "src\NinjaTrader8\Indicators") + (Get-Cs "src\NinjaTrader8\Strategies"))
        } catch { $failures += $_.Exception.Message }
    } else {
        Write-Warning "NinjaTrader assemblies not found (Core/Vendor); skipping NT compile. Strategy NOT compile-checked here."
    }
}

# 7. Run behavior tests
if (-not $SkipTests -and (Test-Path $testsExe) -and ($failures.Count -eq 0)) {
    Write-Host "  [run] behavior tests" -ForegroundColor DarkGray
    $out = & $testsExe
    Write-Host ($out -join "`n")
    if ($LASTEXITCODE -ne 0) { $failures += "Behavior tests FAILED (exit $LASTEXITCODE)" }
}

# 8. Backtester P&L self-test
$sweepExe = Join-Path $binRoot "Intent.Sweep.exe"
if (-not $SkipTests -and (Test-Path $sweepExe) -and ($failures.Count -eq 0)) {
    Write-Host "  [run] backtester self-test" -ForegroundColor DarkGray
    $bt = & $sweepExe --selftest
    Write-Host ($bt -join "`n")
    if ($LASTEXITCODE -ne 0) { $failures += "Backtester self-test FAILED (exit $LASTEXITCODE)" }
}

# 9. Console deserializer self-test
$consoleExe = Join-Path $binRoot "Intent.StreamRunner.exe"
if (-not $SkipTests -and (Test-Path $consoleExe) -and ($failures.Count -eq 0)) {
    Write-Host "  [run] console self-test" -ForegroundColor DarkGray
    $ct = & $consoleExe --selftest
    Write-Host ($ct -join "`n")
    if ($LASTEXITCODE -ne 0) { $failures += "Console self-test FAILED (exit $LASTEXITCODE)" }
}

if ($failures.Count -gt 0) {
    Write-Host "`nBUILD GATE FAILED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nBUILD GATE PASSED (all projects incl. strategy compiled; tests passed)." -ForegroundColor Green
exit 0
