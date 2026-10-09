param(
    [string]$Vrm = "",
    [string]$Config = "",
    [string]$AlphaVersion = $env:VCR_RELEASE_VERSION
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AlphaVersion)) {
    $AlphaVersion = "0.1.0-alpha.1"
}
if ($AlphaVersion -notmatch '^\d+\.\d+\.\d+-alpha\.\d+
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $RepoRoot "Builds/Alpha/$AlphaVersion/Windows/VirtualCharacterRender.exe"

if (-not (Test-Path $Exe -PathType Leaf)) {
    throw "Alpha player not found: $Exe. Run tools/build-alpha.ps1 first."
}

$ArgsList = @()

if ($Vrm) {
    $VrmPath = (Resolve-Path $Vrm).Path
    $ArgsList += "--vcr-vrm=$VrmPath"
}

if ($Config) {
    $ConfigPath = [System.IO.Path]::GetFullPath($Config)
    $ArgsList += "--vcr-config=$ConfigPath"
}

Write-Host "Launching VCR $AlphaVersion"
Write-Host "Player: $Exe"
Write-Host "Webcam, ARKit/iFacialMocap, and VMC inputs are included but start disabled in this alpha scene."

if ($Vrm) {
    Write-Host "VRM: $VrmPath"
}

if ($Config) {
    Write-Host "Config: $ConfigPath"
}

& $Exe @ArgsList
exit $LASTEXITCODE
) {
    throw "Invalid alpha version: $AlphaVersion"
}
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $RepoRoot "Builds/Alpha/$AlphaVersion/Windows/VirtualCharacterRender.exe"

if (-not (Test-Path $Exe -PathType Leaf)) {
    throw "Alpha player not found: $Exe. Run tools/build-alpha.ps1 first."
}

$ArgsList = @()

if ($Vrm) {
    $VrmPath = (Resolve-Path $Vrm).Path
    $ArgsList += "--vcr-vrm=$VrmPath"
}

if ($Config) {
    $ConfigPath = [System.IO.Path]::GetFullPath($Config)
    $ArgsList += "--vcr-config=$ConfigPath"
}

Write-Host "Launching VCR $AlphaVersion"
Write-Host "Player: $Exe"
Write-Host "Webcam, ARKit/iFacialMocap, and VMC inputs are included but start disabled in this alpha scene."

if ($Vrm) {
    Write-Host "VRM: $VrmPath"
}

if ($Config) {
    Write-Host "Config: $ConfigPath"
}

& $Exe @ArgsList
exit $LASTEXITCODE
