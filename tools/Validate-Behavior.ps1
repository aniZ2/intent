$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineDll = Join-Path $repoRoot "src\Intent.Engine\bin\Intent.Engine.dll"
$testsExe = Join-Path $repoRoot "src\Intent.Engine.Tests\bin\Intent.Engine.Tests.exe"
$testsEngineDll = Join-Path $repoRoot "src\Intent.Engine.Tests\bin\Intent.Engine.dll"

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

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $engineDll) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $testsExe) | Out-Null

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

Copy-Item -Force $engineDll $testsEngineDll
Invoke-External -FilePath "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -Arguments @(
    "/target:exe",
    "/nologo",
    "/out:$testsExe",
    "/r:$engineDll",
    (Join-Path $repoRoot "src\Intent.Engine.Tests\Program.cs")
)

& $testsExe
if ($LASTEXITCODE -ne 0) {
    throw "Behavior validation failed."
}

Write-Output "Behavior validation passed."
