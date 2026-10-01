param(
    [Parameter(Mandatory = $true)]
    [string]$Vrm,

    [ValidateSet("Evidence", "Performance")]
    [string]$Mode = "Evidence",

    [string]$ShaderBundle = "",
    [string]$ShaderId = "",
    [string]$MaterialSlot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$vrmPath = (Resolve-Path $Vrm).Path

$folder = if ($Mode -eq "Performance") {
    "Windows-Performance"
} else {
    "Windows-Evidence"
}

$exe = Join-Path $repoRoot "Builds/P0/$folder/VirtualCharacterRender-P0.exe"

if (-not (Test-Path $exe)) {
    throw "P0 player not found: $exe. Build it from Unity first."
}

$argsList = @(
    "--vcr-vrm=$vrmPath"
)

if ($ShaderBundle) {
    $bundlePath = (Resolve-Path $ShaderBundle).Path
    $argsList += "--vcr-shader-bundle=$bundlePath"
}

if ($ShaderId) {
    $argsList += "--vcr-shader-id=$ShaderId"
}

if ($MaterialSlot) {
    $argsList += "--vcr-material-slot=$MaterialSlot"
}

Write-Host "Launching: $exe"
Write-Host "Mode: $Mode"
Write-Host "VRM: $vrmPath"

& $exe @argsList
exit $LASTEXITCODE
