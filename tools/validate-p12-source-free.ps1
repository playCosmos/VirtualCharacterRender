param(
    [string]$UnityEditor = $env:UNITY_EDITOR
)

$ErrorActionPreference = "Stop"
$UnityVersion = "6000.3.25f1"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "unity"
$MediaPipePackage = Join-Path $ProjectPath "Packages/LocalPackages/com.github.homuler.mediapipe-0.16.3.tgz"

if ([string]::IsNullOrWhiteSpace($UnityEditor)) {
    $UnityEditor = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
}

if (-not (Test-Path $UnityEditor -PathType Leaf)) {
    Write-Error "Unity Editor was not found: $UnityEditor"
}

if (-not (Test-Path $MediaPipePackage -PathType Leaf)) {
    Write-Error "Pinned MediaPipe package is missing: $MediaPipePackage"
}

$UnityArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $ProjectPath,
    "-executeMethod", "VCR.Editor.P12.P12BatchValidation.RunSourceFreeAndExit",
    "-logFile", "-"
)

& $UnityEditor @UnityArgs
exit $LASTEXITCODE
