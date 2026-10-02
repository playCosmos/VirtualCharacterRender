param(
    [ValidateSet("Development", "Release")]
    [string]$Mode = "Release",

    [string]$Vrm = "",
    [string]$Config = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$folder = if ($Mode -eq "Development") {
    "Windows-Development"
} else {
    "Windows"
}

$exe = Join-Path $repoRoot "Builds/P1/$folder/VirtualCharacterRender.exe"

if (-not (Test-Path $exe)) {
    throw "P1 player not found: $exe. Build it from Unity first."
}

$argsList = @()

if ($Vrm) {
    $vrmPath = (Resolve-Path $Vrm).Path
    $argsList += "--vcr-vrm=$vrmPath"
}

if ($Config) {
    $configPath = [System.IO.Path]::GetFullPath($Config)
    $argsList += "--vcr-config=$configPath"
}

Write-Host "Launching: $exe"
Write-Host "Mode: $Mode"

if ($Vrm) {
    Write-Host "VRM: $vrmPath"
}

if ($Config) {
    Write-Host "Config: $configPath"
}

& $exe @argsList
exit $LASTEXITCODE
