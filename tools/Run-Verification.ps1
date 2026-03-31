$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineDll = Join-Path $repoRoot "src\Intent.Engine\bin\Intent.Engine.dll"
$consoleDll = Join-Path $repoRoot "src\Intent.Console\bin\Intent.Engine.dll"
$consoleExe = Join-Path $repoRoot "src\Intent.Console\bin\Intent.StreamRunner.exe"
$replayExe = Join-Path $repoRoot "src\Intent.Replay\bin\Intent.Replay.exe"
$sweepExe = Join-Path $repoRoot "src\Intent.Sweep\bin\Intent.Sweep.exe"
$ninjaDll = Join-Path $repoRoot "src\NinjaTrader8\bin\IntentLayerV01.dll"

function Invoke-External {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "External command failed: $FilePath"
    }
}

function Invoke-ScriptChecked {
    param(
        [string]$Path,
        [string[]]$Arguments = @()
    )

    $powerShellExe = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    $invokeArguments = @(
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $Path
    ) + $Arguments

    & $powerShellExe @invokeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Script failed: $Path"
    }
}

function Stop-ProcessQuiet {
    param(
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
            $Process.WaitForExit()
        }
    }
    catch {
    }
}

Write-Output "Refreshing indexes..."
Invoke-ScriptChecked -Path (Join-Path $PSScriptRoot "Refresh-Indexes.ps1")

Write-Output "Checking generated artifacts..."
Invoke-ScriptChecked -Path (Join-Path $PSScriptRoot "Check-GeneratedArtifacts.ps1") -Arguments @("-RepoRoot", $repoRoot)

Write-Output "Analyzing gaps..."
Invoke-ScriptChecked -Path (Join-Path $PSScriptRoot "Analyze-Gaps.ps1") -Arguments @("-RepoRoot", $repoRoot, "-FailOnIssues")

Write-Output "Validating architecture..."
Invoke-ScriptChecked -Path (Join-Path $PSScriptRoot "Validate-Architecture.ps1") -Arguments @("-RepoRoot", $repoRoot, "-FailOnIssues")

Write-Output "Building dependency map..."
Invoke-ScriptChecked -Path (Join-Path $PSScriptRoot "Build-DependencyMap.ps1") -Arguments @("-RepoRoot", $repoRoot)

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $replayExe) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sweepExe) | Out-Null

Write-Output "Compiling standalone engine..."
Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:library",
    "/nologo",
    "/out:$engineDll",
    (Join-Path $repoRoot "src\Intent.Engine\Ingestion\OrderFlowPriceLevel.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Ingestion\IBarBuilder.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Ingestion\BarBuilder.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\TickData.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\OrderFlowData.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\BarData.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Models\EngineSettings.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Transport\TickJsonSerializer.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\State\RollingStatistics.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\State\SessionContext.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\State\EngineState.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Runtime\StreamDecision.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Runtime\TickProcessingResult.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Runtime\IntentRuntime.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Signals\SignalModels.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Signals\SignalMath.cs"),
    (Join-Path $repoRoot "src\Intent.Engine\Signals\IntentSignalEngine.cs")
)

Write-Output "Compiling standalone console..."
Copy-Item -Force $engineDll $consoleDll
Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:exe",
    "/nologo",
    "/out:$consoleExe",
    "/r:$engineDll",
    "/r:System.Runtime.Serialization.dll",
    (Join-Path $repoRoot "src\Intent.Console\DecisionPacketSink.cs"),
    (Join-Path $repoRoot "src\Intent.Console\DashboardBroadcaster.cs"),
    (Join-Path $repoRoot "src\Intent.Console\Program.cs"),
    (Join-Path $repoRoot "src\Intent.Console\RawTickArchive.cs"),
    (Join-Path $repoRoot "src\Intent.Console\RunnerOptions.cs"),
    (Join-Path $repoRoot "src\Intent.Console\RunnerLogger.cs"),
    (Join-Path $repoRoot "src\Intent.Console\RuntimeFactory.cs"),
    (Join-Path $repoRoot "src\Intent.Console\TickJsonDeserializer.cs"),
    (Join-Path $repoRoot "src\Intent.Console\TcpTickServer.cs")
)

Write-Output "Standalone stream runner compiled."

Write-Output "Compiling replay client..."
Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:exe",
    "/nologo",
    "/out:$replayExe",
    "/r:System.Runtime.Serialization.dll",
    (Join-Path $repoRoot "src\Intent.Replay\Program.cs"),
    (Join-Path $repoRoot "src\Intent.Replay\ReplayOptions.cs"),
    (Join-Path $repoRoot "src\Intent.Replay\TickReplayClient.cs")
)

Write-Output "Compiling sweep client..."
Copy-Item -Force $engineDll (Join-Path (Split-Path -Parent $sweepExe) "Intent.Engine.dll")
Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:exe",
    "/nologo",
    "/out:$sweepExe",
    "/r:$engineDll",
    "/r:System.Runtime.Serialization.dll",
    (Join-Path $repoRoot "src\Intent.Sweep\Program.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\SweepOptions.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\TickFileReader.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\ParameterSweepRunner.cs"),
    (Join-Path $repoRoot "src\Intent.Sweep\SweepSummary.cs")
)

Write-Output "Running replay smoke test..."
$smokeDir = Join-Path ([System.IO.Path]::GetTempPath()) ("intent-replay-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
$smokeTicksPath = Join-Path $smokeDir "ticks.ndjson"
$smokePacketsPath = Join-Path $smokeDir "packets.ndjson"
$smokeTickArchiveDir = Join-Path $smokeDir "tick-archive"
$smokeDashboardPort = 4118
$smokeRunnerLogPath = Join-Path $smokeDir "runner.log"
$smokeRunnerOutPath = Join-Path $smokeDir "runner.out.log"
$smokeRunnerErrPath = Join-Path $smokeDir "runner.err.log"
$smokeReplayOutPath = Join-Path $smokeDir "replay.out.log"
$smokePort = 4117
$runnerProcess = $null

try {
    @(
        '{"timestampUtc":"2026-03-30T14:30:00.0000000Z","instrument":"ES 06-26","price":100.50,"volume":100,"bid":100.25,"ask":100.50,"isBuyerInitiated":true}',
        '{"timestampUtc":"2026-03-30T14:30:20.0000000Z","instrument":"ES 06-26","price":100.25,"volume":110,"bid":100.00,"ask":100.25,"isBuyerInitiated":false}',
        '{"timestampUtc":"2026-03-30T14:30:40.0000000Z","instrument":"ES 06-26","price":100.00,"volume":120,"bid":99.75,"ask":100.00,"isBuyerInitiated":false}',
        '{"timestampUtc":"2026-03-30T14:31:00.0000000Z","instrument":"ES 06-26","price":100.00,"volume":100,"bid":99.75,"ask":100.00,"isBuyerInitiated":false}',
        '{"timestampUtc":"2026-03-30T14:31:20.0000000Z","instrument":"ES 06-26","price":100.25,"volume":105,"bid":100.00,"ask":100.25,"isBuyerInitiated":true}',
        '{"timestampUtc":"2026-03-30T14:31:40.0000000Z","instrument":"ES 06-26","price":100.50,"volume":115,"bid":100.25,"ask":100.50,"isBuyerInitiated":true}'
    ) | Set-Content -Path $smokeTicksPath -Encoding UTF8

    $runnerProcess = Start-Process -FilePath $consoleExe -ArgumentList @("--port", $smokePort, "--packet-file", $smokePacketsPath, "--tick-archive-dir", $smokeTickArchiveDir, "--dashboard-port", $smokeDashboardPort, "--log-file", $smokeRunnerLogPath) -RedirectStandardOutput $smokeRunnerOutPath -RedirectStandardError $smokeRunnerErrPath -PassThru
    Start-Sleep -Milliseconds 750

    $dashboardReady = $false
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            $dashboardResponse = Invoke-WebRequest -UseBasicParsing -Uri ("http://127.0.0.1:{0}/" -f $smokeDashboardPort)
            if ($dashboardResponse.Content -match "Intent Dashboard") {
                $dashboardReady = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 300
        }
    }

    if (-not $dashboardReady) {
        throw "Replay smoke test failed: dashboard endpoint did not return expected content."
    }

    Invoke-External -FilePath $replayExe -Arguments @("--input", $smokeTicksPath, "--port", $smokePort)
    Start-Sleep -Milliseconds 750
}
finally {
    Stop-ProcessQuiet -Process $runnerProcess
}

if (-not (Test-Path $smokePacketsPath)) {
    throw "Replay smoke test failed: no packet output file was created."
}

$smokePackets = Get-Content $smokePacketsPath
if ($smokePackets.Count -lt 1) {
    throw "Replay smoke test failed: stream runner emitted no packets."
}

if (-not ($smokePackets -match '"eventType":"barClose"')) {
    throw "Replay smoke test failed: expected at least one barClose packet."
}

$archivedTickFiles = Get-ChildItem -Path $smokeTickArchiveDir -Filter *.ndjson -Recurse -ErrorAction SilentlyContinue
if ($null -eq $archivedTickFiles -or $archivedTickFiles.Count -lt 1) {
    throw "Replay smoke test failed: expected archived raw ticks."
}

Write-Output "Running parameter sweep smoke test..."
$smokeSweepOutputPath = Join-Path $smokeDir "sweep.ndjson"
Invoke-External -FilePath $sweepExe -Arguments @(
    "--input", $smokeTicksPath,
    "--output", $smokeSweepOutputPath,
    "--signal-thresholds", "55,60",
    "--imbalance-weights", "0.25,0.35"
)

if (-not (Test-Path $smokeSweepOutputPath)) {
    throw "Parameter sweep smoke test failed: no output file was created."
}

$smokeSweepLines = Get-Content $smokeSweepOutputPath
if ($smokeSweepLines.Count -lt 6) {
    throw "Parameter sweep smoke test failed: expected config summaries plus default and ranking output."
}

if (-not ($smokeSweepLines -match '"averageLatencyMs"')) {
    throw "Parameter sweep smoke test failed: expected latency metrics in output."
}

if (-not ($smokeSweepLines -match '"f1"')) {
    throw "Parameter sweep smoke test failed: expected F1 metrics in output."
}

if (-not ($smokeSweepLines -match '"recordType":"ranking"')) {
    throw "Parameter sweep smoke test failed: expected final ranking output."
}

Remove-Item -Recurse -Force $smokeDir

Write-Output "Running behavior validation..."
Invoke-ScriptChecked -Path (Join-Path $PSScriptRoot "Validate-Behavior.ps1")

Write-Output "Compiling NinjaTrader adapter..."
Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/define:STANDALONE_VERIFY",
    "/target:library",
    "/nologo",
    "/out:$ninjaDll",
    "/r:$engineDll",
    "/r:C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Core.dll",
    "/r:C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Gui.dll",
    "/r:$HOME\Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Vendor.dll",
    "/r:C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF\WindowsBase.dll",
    "/r:C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF\PresentationCore.dll",
    "/r:C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF\PresentationFramework.dll",
    "/r:System.ComponentModel.DataAnnotations.dll",
    (Join-Path $repoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Adapter.cs"),
    (Join-Path $repoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Models.cs"),
    (Join-Path $repoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Engine.cs"),
    (Join-Path $repoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Rendering.cs"),
    (Join-Path $repoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.Streaming.cs"),
    (Join-Path $repoRoot "src\NinjaTrader8\Indicators\IntentLayerV01.cs")
)

Write-Output "Verification complete."
