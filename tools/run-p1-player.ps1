param(
    [ValidateSet("Development", "Release", "IL2CPP")]
    [string]$Mode = "Release",

    [string]$Vrm = "",
    [string]$Config = "",
    [string]$PlayerDirectory = "",
    [string]$EvidenceDirectory = ""
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

$argsList = @()
if ($Vrm) {
    $vrmPath = (Resolve-Path -LiteralPath $Vrm -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $vrmPath -PathType Leaf)) {
        throw "VRM file missing: $vrmPath"
    }
    $argsList += "--vcr-vrm=$vrmPath"
}
if ($Config) {
    $configPath = [System.IO.Path]::GetFullPath($Config)
    $argsList += "--vcr-config=$configPath"
}

$evidenceRoot = $null
if ($EvidenceDirectory) {
    $evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    $argsList += "-logFile"
    $argsList += (Join-Path $evidenceRoot "Player.log")
}

Write-Host "Launching P1 $Mode Player: $exe"
Write-Host "Executable SHA-256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
if ($Vrm) { Write-Host "VRM: $vrmPath" }
if ($Config) { Write-Host "Config: $configPath" }
if ($evidenceRoot) { Write-Host "Local evidence directory: $evidenceRoot" }

& $exe @argsList
$exitCode = $LASTEXITCODE

if ($evidenceRoot) {
    [ordered]@{
        mode = $Mode
        playerExecutableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        exitCode = $exitCode
        utcRecordedAt = [DateTime]::UtcNow.ToString("o")
        physicalHumanAcceptance = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot "launch-evidence.json") -Encoding UTF8
    Write-Host "Review Player.log and capture manual UI/IME/hardware results separately."
}
exit $exitCode
