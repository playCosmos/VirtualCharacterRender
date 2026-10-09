param(
    [string]$UnityEditor = $env:UNITY_EDITOR,
    [switch]$SkipMediaPipeBootstrap,
    [string]$AlphaVersion = $env:VCR_RELEASE_VERSION
)

$ErrorActionPreference = "Stop"

$UnityVersion = "6000.3.25f1"
if ([string]::IsNullOrWhiteSpace($AlphaVersion)) {
    $AlphaVersion = "0.1.0-alpha.1"
}
if ($AlphaVersion -notmatch '^\d+\.\d+\.\d+-alpha\.\d+
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "unity"
$MediaPipePackage = Join-Path $ProjectPath "Packages/LocalPackages/com.github.homuler.mediapipe-0.16.3.tgz"
$WindowsDir = Join-Path $RepoRoot "Builds/Alpha/$AlphaVersion/Windows"
$Exe = Join-Path $WindowsDir "VirtualCharacterRender.exe"
$Zip = Join-Path $RepoRoot "Builds/Alpha/VirtualCharacterRender-$AlphaVersion-Windows-x64.zip"

if ([string]::IsNullOrWhiteSpace($UnityEditor)) {
    $UnityEditor = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
}

if (-not (Test-Path $UnityEditor -PathType Leaf)) {
    Write-Error "Unity Editor was not found: $UnityEditor"
}

if (-not (Test-Path $MediaPipePackage -PathType Leaf)) {
    if ($SkipMediaPipeBootstrap) {
        Write-Error "Pinned MediaPipe package is missing: $MediaPipePackage"
    }

    Write-Host "Pinned MediaPipe package is missing. Bootstrapping it now..."
    & (Join-Path $PSScriptRoot "bootstrap-mediapipe.ps1")

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$UnityArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $ProjectPath,
    "-executeMethod", "VCR.Editor.Alpha.AlphaBuildMenu.BuildWindowsAndExit",
    "-logFile", "-"
)

& $UnityEditor @UnityArgs
$UnityExit = $LASTEXITCODE

if ($UnityExit -ne 0) {
    exit $UnityExit
}

if (-not (Test-Path $Exe -PathType Leaf)) {
    Write-Error "Unity reported success but the alpha player was not found: $Exe"
}

$ZipDirectory = Split-Path -Parent $Zip
New-Item -ItemType Directory -Force -Path $ZipDirectory | Out-Null

if (Test-Path $Zip) {
    Remove-Item $Zip -Force
}

Compress-Archive -Path (Join-Path $WindowsDir "*") -DestinationPath $Zip -CompressionLevel Optimal

Write-Host ""
Write-Host "VCR alpha build complete."
Write-Host "Player: $Exe"
Write-Host "Package: $Zip"
Write-Host "Physical webcam/ARKit evidence was not required by this alpha build gate."

exit 0
) {
    throw "Invalid alpha version: $AlphaVersion"
}
$env:VCR_RELEASE_VERSION = $AlphaVersion
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "unity"
$MediaPipePackage = Join-Path $ProjectPath "Packages/LocalPackages/com.github.homuler.mediapipe-0.16.3.tgz"
$WindowsDir = Join-Path $RepoRoot "Builds/Alpha/$AlphaVersion/Windows"
$Exe = Join-Path $WindowsDir "VirtualCharacterRender.exe"
$Zip = Join-Path $RepoRoot "Builds/Alpha/VirtualCharacterRender-$AlphaVersion-Windows-x64.zip"

if ([string]::IsNullOrWhiteSpace($UnityEditor)) {
    $UnityEditor = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
}

if (-not (Test-Path $UnityEditor -PathType Leaf)) {
    Write-Error "Unity Editor was not found: $UnityEditor"
}

if (-not (Test-Path $MediaPipePackage -PathType Leaf)) {
    if ($SkipMediaPipeBootstrap) {
        Write-Error "Pinned MediaPipe package is missing: $MediaPipePackage"
    }

    Write-Host "Pinned MediaPipe package is missing. Bootstrapping it now..."
    & (Join-Path $PSScriptRoot "bootstrap-mediapipe.ps1")

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$UnityArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $ProjectPath,
    "-executeMethod", "VCR.Editor.Alpha.AlphaBuildMenu.BuildWindowsAndExit",
    "-logFile", "-"
)

& $UnityEditor @UnityArgs
$UnityExit = $LASTEXITCODE

if ($UnityExit -ne 0) {
    exit $UnityExit
}

if (-not (Test-Path $Exe -PathType Leaf)) {
    Write-Error "Unity reported success but the alpha player was not found: $Exe"
}

$ZipDirectory = Split-Path -Parent $Zip
New-Item -ItemType Directory -Force -Path $ZipDirectory | Out-Null

if (Test-Path $Zip) {
    Remove-Item $Zip -Force
}

Compress-Archive -Path (Join-Path $WindowsDir "*") -DestinationPath $Zip -CompressionLevel Optimal

Write-Host ""
Write-Host "VCR alpha build complete."
Write-Host "Player: $Exe"
Write-Host "Package: $Zip"
Write-Host "Physical webcam/ARKit evidence was not required by this alpha build gate."

exit 0
