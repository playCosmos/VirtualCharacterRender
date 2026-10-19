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
    Write-Error "Unity Editor was not found: $UnityEditor. Pass -UnityEditor <path> or set UNITY_EDITOR."
}

if (-not (Test-Path $MediaPipePackage -PathType Leaf)) {
    Write-Error "Pinned MediaPipe package is missing: $MediaPipePackage. Run .\tools\bootstrap-mediapipe.ps1 first."
}

Write-Host "Running VCR P2 source-free validation with:"
Write-Host "  Unity: $UnityEditor"
Write-Host "  Project: $ProjectPath"

$UnityArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $ProjectPath,
    "-executeMethod", "VCR.Editor.P2.P2BatchValidation.RunSourceFreeAndExit",
    "-logFile", "-"
)

if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    # Windows PowerShell does not wait for the GUI-subsystem Unity.exe and
    # never sets $LASTEXITCODE for it. Run Unity synchronously instead.
    $UnityArguments = ($UnityArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $Status = (Start-Process -FilePath $UnityEditor -ArgumentList $UnityArguments -NoNewWindow -Wait -PassThru).ExitCode
}
else {
    & $UnityEditor @UnityArgs
    $Status = $LASTEXITCODE
}

if ($Status -eq 0) {
    Write-Host "VCR P2 source-free batch validation: PASS"
} else {
    Write-Error "VCR P2 source-free batch validation: FAIL (exit=$Status)"
}

exit $Status
