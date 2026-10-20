$ErrorActionPreference = "Stop"

$Version = "0.16.3"
$ExpectedSha256 = "cc3e77a219e0b99618ae3be64c31a566197deedc69c1e136acf52d65d7cf2e79"
$Url = "https://github.com/homuler/MediaPipeUnityPlugin/releases/download/v$Version/com.github.homuler.mediapipe-$Version.tgz"
$ModelNames = @(
    "face_landmarker_v2_with_blendshapes.bytes",
    "holistic_landmarker.bytes"
)

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TargetDir = Join-Path $RepoRoot "unity/Packages/LocalPackages"
$Target = Join-Path $TargetDir "com.github.homuler.mediapipe-$Version.tgz"
$ModelDir = Join-Path $RepoRoot "unity/Assets/StreamingAssets/VCR/Models"

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
    throw "tar is required to extract MediaPipe models from the pinned package."
}

$Entries = & tar -tf $Target
$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("vcr-mediapipe-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

try {
    foreach ($ModelName in $ModelNames) {
        $ModelEntry = $Entries |
            Where-Object { $_ -match ("(^|/)" + [regex]::Escape($ModelName) + "$") } |
            Select-Object -First 1

        if (-not $ModelEntry) {
            throw "$ModelName was not found in $Target"
        }

        & tar -xf $Target -C $TempDir $ModelEntry
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to extract $ModelEntry"
        }

        $RelativePath = $ModelEntry -replace '/', [System.IO.Path]::DirectorySeparatorChar
        $ExtractedModel = Join-Path $TempDir $RelativePath
        $ModelTarget = Join-Path $ModelDir $ModelName
        Copy-Item -Force $ExtractedModel $ModelTarget
        Write-Host "Prepared model: $ModelTarget"
    }
} finally {
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
}
