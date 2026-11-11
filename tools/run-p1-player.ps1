param(
    [ValidateSet("Development", "Release", "IL2CPP")]
    [string]$Mode = "Release",

    [string]$Vrm = "",
    [string]$Config = "",
    [string]$PlayerDirectory = "",
    [string]$EvidenceDirectory = "",
    [string]$ManifestFile = "",
    [string]$ExpectedSourceSha = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$folder = switch ($Mode) {
    "Development" { "Windows-Development" }
    "IL2CPP" { "Windows-IL2CPP" }
    default { "Windows" }
}

# Unity's project root is <repo>/unity, not the repository root.
# Allow a separately extracted, source-bound CI Player to be checked as-is.
$playerRoot = if ($PlayerDirectory) {
    [System.IO.Path]::GetFullPath($PlayerDirectory)
} else {
    Join-Path $repoRoot "unity/Builds/P1/$folder"
}
$exe = Join-Path $playerRoot "VirtualCharacterRender.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "P1 Player not found: $exe. Generate/build the P1 scene or supply -PlayerDirectory."
}

# Reject a mislabeled Mono/Alpha build before calling it IL2CPP acceptance.
if ($Mode -eq "IL2CPP") {
    foreach ($relative in @(
        "GameAssembly.dll",
        "UnityPlayer.dll",
        "VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $playerRoot $relative) -PathType Leaf)) {
            throw "P1 IL2CPP binary is incomplete: $relative"
        }
    }
    if (Test-Path -LiteralPath (Join-Path $playerRoot "MonoBleedingEdge")) {
        throw "P1 IL2CPP binary unexpectedly contains the Mono runtime."
    }
}

# Verify the complete extracted Player against the CI-produced manifest when
# supplied. An EXE hash or the presence of IL2CPP DLLs alone is insufficient.
$manifestEvidence = $null
if ($ExpectedSourceSha -and -not $ManifestFile) {
    throw '-ExpectedSourceSha requires -ManifestFile.'
}
if ($ManifestFile) {
    $manifestEvidence = & (Join-Path $PSScriptRoot 'verify-p1-player-payload.ps1') `
        -PlayerDirectory $playerRoot `
        -ManifestFile $ManifestFile `
        -ExpectedSourceSha $ExpectedSourceSha
    Write-Host "Verified $($manifestEvidence.fileCount) P1 payload files against manifest SHA-256 $($manifestEvidence.manifestSha256)"
} elseif ($Mode -eq 'IL2CPP') {
    Write-Warning 'IL2CPP payload hashes UNVERIFIED; supply -ManifestFile and -ExpectedSourceSha for a source-bound run.'
}

$argsList = @()
if ($Vrm) {
    $vrmPath = (Resolve-Path -LiteralPath $Vrm -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $vrmPath -PathType Leaf)) {
        throw "VRM file missing: $vrmPath"
    }
    $argsList += "--vcr-vrm=$vrmPath"
}
if ($Config) {
    if (-not (Test-Path -LiteralPath $Config -PathType Leaf)) {
        throw "P1 config file missing: $Config"
    }
    $configPath = (Resolve-Path -LiteralPath $Config -ErrorAction Stop).Path
    $argsList += "--vcr-config=$configPath"
}

$evidenceRoot = $null
if ($EvidenceDirectory) {
    $evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    $argsList += "-logFile"
    $argsList += (Join-Path $evidenceRoot "Player.log")
    # Prevent a previous attempt from leaving success-looking evidence.
    foreach ($name in @("Player.log", "launch-evidence.json")) {
        $old = Join-Path $evidenceRoot $name
        if (Test-Path -LiteralPath $old) {
            Remove-Item -LiteralPath $old -Force
        }
    }
}

Write-Host "Launching P1 $Mode Player: $exe"
Write-Host "Executable SHA-256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
if ($Vrm) { Write-Host "VRM: $vrmPath" }
if ($Config) { Write-Host "Config: $configPath" }
if ($evidenceRoot) { Write-Host "Local evidence directory: $evidenceRoot" }

# Unity is a GUI-subsystem process. Invoke it with an explicit Wait
# rather than assuming every Windows PowerShell host waits for native GUI
# applications before returning an exit code.
# Quote complete arguments so VRM/config/evidence paths may contain spaces.
$launchOptions = @{
    FilePath = $exe
    WorkingDirectory = $playerRoot
    PassThru = $true
    Wait = $true
}
if ($argsList.Count -gt 0) {
    $launchOptions.ArgumentList = @($argsList | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    })
}
$playerProcess = Start-Process @launchOptions
$exitCode = $playerProcess.ExitCode

if ($evidenceRoot) {
    $logPath = Join-Path $evidenceRoot "Player.log"
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf) -or
        (Get-Item -LiteralPath $logPath).Length -eq 0) {
        throw "P1 Player did not produce a fresh nonempty Player.log: $logPath (exit=$exitCode)"
    }
    [ordered]@{
        mode = $Mode
        playerExecutableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        payloadManifestVerified = ($null -ne $manifestEvidence)
        payloadManifestSha256 = if ($manifestEvidence) { $manifestEvidence.manifestSha256 } else { $null }
        expectedSourceSha = if ($manifestEvidence) { $manifestEvidence.sourceSha } else { $null }
        payloadFileCount = if ($manifestEvidence) { $manifestEvidence.fileCount } else { $null }
        playerLogSha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
        playerLogBytes = (Get-Item -LiteralPath $logPath).Length
        exitCode = $exitCode
        utcRecordedAt = [DateTime]::UtcNow.ToString("o")
        physicalHumanAcceptance = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot "launch-evidence.json") -Encoding UTF8
    Write-Host "Review Player.log and capture manual UI/IME/hardware results separately."
}
exit $exitCode
