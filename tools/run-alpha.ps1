param(
    [string]$Vrm = "",
    [string]$Config = "",
    [string]$AlphaVersion = $env:VCR_RELEASE_VERSION,
    [string]$PlayerPath = "",
    [switch]$ProfileExit
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AlphaVersion)) {
    $AlphaVersion = "0.1.0-alpha.1"
}
if ($AlphaVersion -notmatch '^\d+\.\d+\.\d+-alpha\.\d+$') {
    throw "Invalid alpha version: $AlphaVersion"
}
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Exe = if ([string]::IsNullOrWhiteSpace($PlayerPath)) {
    Join-Path $RepoRoot "Builds/Alpha/$AlphaVersion/Windows/VirtualCharacterRender.exe"
} else {
    [System.IO.Path]::GetFullPath($PlayerPath)
}

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

$Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
if ($ProfileExit) {
    # Existing public alpha binaries can be profiled without rebuilding.
    $LogPath = Join-Path ([System.IO.Path]::GetTempPath()) (
        "vcr-exit-" + [System.Guid]::NewGuid().ToString("N") + ".log")
    $ArgsList += "-logFile"
    $ArgsList += $LogPath
    Write-Host "Exit diagnostic log: $LogPath"
}

if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    # Windows PowerShell does not wait for GUI-subsystem processes and
    # never sets $LASTEXITCODE for them. Run the player synchronously instead.
    $CommandLine = ($ArgsList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $ExitCode = (Start-Process -FilePath $Exe -ArgumentList $CommandLine -NoNewWindow -Wait -PassThru).ExitCode
}
else {
    & $Exe @ArgsList
    $ExitCode = $LASTEXITCODE
}
$Stopwatch.Stop()

if ($ProfileExit) {
    Write-Host ("Native process lifetime (startup + shutdown): {0} ms" -f $Stopwatch.ElapsedMilliseconds)
    if (Test-Path $LogPath -PathType Leaf) {
        $Recorded = Select-String -Path $LogPath -Pattern (
            'VCR (exit:|shutdown:|shutdown step end:|MediaPipe cleanup step end:|UniWinC shutdown:)')
        $Recorded | Select-Object -Last 80 | ForEach-Object { Write-Host $_.Line }
        Write-Host "Measure request-to-cleanup with the VCR exit markers above."
        Write-Host "Process lifetime includes startup, not just shutdown."
    } else {
        Write-Warning "Unity did not produce the requested diagnostic log."
    }
}
exit $ExitCode
