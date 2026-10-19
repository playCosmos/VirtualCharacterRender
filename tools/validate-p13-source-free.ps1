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
    "-executeMethod", "VCR.Editor.P13.P13BatchValidation.RunSourceFreeAndExit",
    "-logFile", "-"
)

if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    # Windows PowerShell does not wait for the GUI-subsystem Unity.exe and
    # never sets $LASTEXITCODE for it. Run Unity synchronously instead.
    $UnityArguments = ($UnityArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $UnityExit = (Start-Process -FilePath $UnityEditor -ArgumentList $UnityArguments -NoNewWindow -Wait -PassThru).ExitCode
}
else {
    & $UnityEditor @UnityArgs
    $UnityExit = $LASTEXITCODE
}

exit $UnityExit
