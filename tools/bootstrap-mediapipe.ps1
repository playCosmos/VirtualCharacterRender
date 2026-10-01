$ErrorActionPreference = "Stop"

$Version = "0.16.3"
$ExpectedSha256 = "cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79"
$Url = "https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v$Version/com.github.homuler.mediapipe-$Version.tgz"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TargetDir = Join-Path $RepoRoot "unity/Packages/LocalPackages"
$Target = Join-Path $TargetDir "com.github.homuler.mediapipe-$Version.tgz"

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

if (Test-Path $Target) {
    $Current = (Get-FileHash -Algorithm SHA256 $Target).Hash.ToLowerInvariant()
    if ($Current -eq $ExpectedSha256) {
        Write-Host "MediaPipeUnityPlugin $Version already present and verified."
        exit 0
    }

    Remove-Item -Force $Target
}

Write-Host "Downloading MediaPipeUnityPlugin $Version..."
Invoke-WebRequest -Uri $Url -OutFile $Target

$Actual = (Get-FileHash -Algorithm SHA256 $Target).Hash.ToLowerInvariant()
if ($Actual -ne $ExpectedSha256) {
    Remove-Item -Force $Target
    throw "SHA-256 mismatch. Expected $ExpectedSha256, got $Actual"
}

Write-Host "Verified: $Target"
