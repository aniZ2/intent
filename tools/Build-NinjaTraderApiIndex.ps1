param(
    [string]$OutputPath = "",
    [string]$CustomRoot = "$HOME\Documents\NinjaTrader 8\bin\Custom",
    [string]$InstallRoot = "C:\Program Files\NinjaTrader 8\bin"
)

$ErrorActionPreference = "Stop"

if (-not $OutputPath) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $OutputPath = Join-Path $repoRoot "docs\NINJATRADER_API_INDEX.md"
}

$assemblyPaths = @(
    (Join-Path $InstallRoot "Infralution.Localization.Wpf.dll"),
    (Join-Path $InstallRoot "InfragisticsWPF.DataPresenter.dll"),
    (Join-Path $InstallRoot "NinjaTrader.Core.dll"),
    (Join-Path $InstallRoot "NinjaTrader.Gui.dll"),
    (Join-Path $CustomRoot "NinjaTrader.Custom.dll"),
    (Join-Path $CustomRoot "NinjaTrader.Vendor.dll")
)

foreach ($path in $assemblyPaths) {
    if (Test-Path $path) {
        [Reflection.Assembly]::LoadFrom($path) | Out-Null
    }
}

function Get-TypeOrThrow {
    param([string]$FullName)

    $type = [AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object {
            try { $_.GetType($FullName, $false) } catch { $null }
        } |
        Where-Object { $_ } |
        Select-Object -First 1

    if (-not $type) {
        throw "Type not found: $FullName"
    }

    return $type
}

$typesToIndex = @(
    "NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType",
    "NinjaTrader.NinjaScript.BarsTypes.VolumetricData",
    "NinjaTrader.NinjaScript.Indicators.OrderFlowCumulativeDelta",
    "NinjaTrader.Data.Bars"
)

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# NinjaTrader API Index")
$lines.Add("")
$lines.Add(("Generated: {0:yyyy-MM-dd HH:mm:ss zzz}" -f (Get-Date)))
$lines.Add("")
$lines.Add("This snapshot is generated from local installed assemblies.")
$lines.Add("")

foreach ($typeName in $typesToIndex) {
    $type = Get-TypeOrThrow -FullName $typeName
    $lines.Add(("## {0}" -f $type.FullName))
    $lines.Add("")
    $lines.Add(('Assembly: `{0}`' -f $type.Assembly.GetName().Name))
    $lines.Add("")
    $lines.Add("### Properties")
    $lines.Add("")

    $properties = $type.GetProperties([Reflection.BindingFlags] "Instance,Public,DeclaredOnly")
    if (-not $properties -or $properties.Count -eq 0) {
        $lines.Add("- none")
    } else {
        foreach ($property in $properties | Sort-Object Name) {
            $lines.Add(('- `{0}`: `{1}`' -f $property.Name, $property.PropertyType.Name))
        }
    }

    $lines.Add("")
    $lines.Add("### Methods")
    $lines.Add("")

    $methods = $type.GetMethods([Reflection.BindingFlags] "Instance,Public,DeclaredOnly") |
        Where-Object { -not $_.IsSpecialName } |
        Sort-Object Name

    if (-not $methods -or $methods.Count -eq 0) {
        $lines.Add("- none")
    } else {
        foreach ($method in $methods) {
            $parameterList = ($method.GetParameters() | ForEach-Object { "{0} {1}" -f $_.ParameterType.Name, $_.Name }) -join ", "
            $lines.Add(('- `{0}({1}) -> {2}`' -f $method.Name, $parameterList, $method.ReturnType.Name))
        }
    }

    $lines.Add("")
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Set-Content -Path $OutputPath -Value $lines -Encoding UTF8
Write-Output "Wrote $OutputPath"
