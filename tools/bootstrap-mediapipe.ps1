$ErrorActionPreference = "Stop"

$Version = "0.16.3"
$ExpectedSha256 = "cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79"
$Url = "https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v$Version/com.github.homuler.mediapipe-$Version.tgz"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TargetDir = Join-Path $RepoRoot "unity/Packages/LocalPackages"
$Target = Join-Path $TargetDir "com.github.homuler.mediapipe-$Version.tgz"
$ModelDir = Join-Path $RepoRoot "unity/Assets/StreamingAssets/VCR/Models"
$ModelTarget = Join-Path $ModelDir "holistic_landmarker.bytes"

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
New-Item -ItemType Directory -Force -Path $ModelDir | Out-Null

$NeedsDownload = $true
if (Test-Path $Target) {
    $Current = (Get-FileHash -Algorithm SHA256 $Target).Hash.ToLowerInvariant()
    if ($Current -eq $ExpectedSha256) {
        $NeedsDownload = $false
        Write-Host "MediaPipeUnityPlugin $Version already present and verified."
    } else {
        Remove-Item -Force $Target
    }
}

if ($NeedsDownload) {
    Write-Host "Downloading MediaPipeUnityPlugin $Version..."
    Invoke-WebRequest -Uri $Url -OutFile $Target

    $Actual = (Get-FileHash -Algorithm SHA256 $Target).Hash.ToLowerInvariant()
    if ($Actual -ne $ExpectedSha256) {
        Remove-Item -Force $Target
        throw "SHA-256 mismatch. Expected $ExpectedSha256, got $Actual"
    }

    Write-Host "Verified: $Target"
}

if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    throw "tar is required to extract holistic_landmarker.bytes from the pinned package."
}

$Entries = & tar -tf $Target
$ModelEntry = $Entries |
    Where-Object { $_ -match '(^|/)holistic_landmarker\.bytes$' } |
    Select-Object -First 1

if (-not $ModelEntry) {
    throw "holistic_landmarker.bytes was not found in $Target"
}

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("vcr-mediapipe-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

try {
    & tar -xf $Target -C $TempDir $ModelEntry
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to extract $ModelEntry"
    }

    $RelativePath = $ModelEntry -replace '/', [System.IO.Path]::DirectorySeparatorChar
    $ExtractedModel = Join-Path $TempDir $RelativePath
    Copy-Item -Force $ExtractedModel $ModelTarget
} finally {
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
}

Write-Host "Prepared model: $ModelTarget"
