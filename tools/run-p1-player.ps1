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


# A complete extracted-file manifest is generated beside the CI windows.zip.
# Validate it against an independently supplied source SHA; the manifest
# alone is not evidence of publisher identity or a trusted download.
$manifestVerified = $false
$manifestSha256 = $null
$validatedSourceSha = $null
if ($ExpectedSourceSha -and -not $ManifestFile) {
    throw 'ExpectedSourceSha requires -ManifestFile.'
}
if ($ManifestFile) {
    if ($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}if ($Vrm) {
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
    # Never let a previous run provide a success-looking log or record.
    foreach ($oldEvidence in @("Player.log", "launch-evidence.json")) {
        $oldPath = Join-Path $evidenceRoot $oldEvidence
        if (Test-Path -LiteralPath $oldPath) {
            Remove-Item -LiteralPath $oldPath -Force
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
        payloadManifestVerified = $manifestVerified
        payloadManifestSha256 = $manifestSha256
        expectedSourceSha = $validatedSourceSha
        playerLogSha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
        playerLogBytes = (Get-Item -LiteralPath $logPath).Length
        exitCode = $exitCode
        utcRecordedAt = [DateTime]::UtcNow.ToString("o")
        physicalHumanAcceptance = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot "launch-evidence.json") -Encoding UTF8
    Write-Host "Review Player.log and capture manual UI/IME/hardware results separately."
}
exit $exitCode
) {
        throw 'Source-bound manifest validation requires a 40-character -ExpectedSourceSha.'
    }
    $manifestPath = (Resolve-Path -LiteralPath $ManifestFile -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "P1 payload manifest is not a file: $ManifestFile"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.format -ne 'vcr-p1-payload-sha256-v1' -or
        $manifest.sourceSha -notmatch '^[0-9a-fA-F]{40}if ($Vrm) {
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
 -or
        $manifest.sourceSha -ine $ExpectedSourceSha -or
        $null -eq $manifest.files -or @($manifest.files).Count -eq 0) {
        throw 'P1 payload manifest has an invalid format, source SHA or file inventory.'
    }
    $expectedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $rootPrefix = [IO.Path]::GetFullPath($playerRoot).TrimEnd([char[]]@('\', '/')) + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in $manifest.files) {
        $relative = [string]$entry.path
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('\') -or
            $relative.Contains(':') -or $relative.StartsWith('/') -or
            $relative -match '[\x00-\x1f]' -or
            @($relative.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -gt 0 -or
            -not $expectedPaths.Add($relative)) {
            throw "Unsafe or duplicate P1 payload path: $relative"
        }
        $full = [IO.Path]::GetFullPath((Join-Path $playerRoot ($relative.Replace('/', '\'))))
        if (-not $full.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $full -PathType Leaf)) {
            throw "Missing P1 payload file: $relative"
        }
        $item = Get-Item -LiteralPath $full -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $null -eq $entry.size -or [long]$entry.size -ne $item.Length -or
            [string]$entry.sha256 -notmatch '^[0-9a-fA-F]{64}if ($Vrm) {
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
 -or
            (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ine $entry.sha256) {
            throw "P1 payload hash/size mismatch: $relative"
        }
    }
    $actualFiles = @(Get-ChildItem -LiteralPath $playerRoot -File -Recurse -Force)
    if ($actualFiles.Count -ne $expectedPaths.Count) {
        throw "P1 payload file count mismatch: expected $($expectedPaths.Count), found $($actualFiles.Count)"
    }
    foreach ($item in $actualFiles) {
        $relative = $item.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        if (-not $expectedPaths.Contains($relative)) {
            throw "Unexpected P1 payload file: $relative"
        }
    }
    $reparsePoints = @(Get-ChildItem -LiteralPath $playerRoot -Recurse -Force |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($reparsePoints.Count -gt 0) {
        throw 'P1 payload contains a reparse point; cannot assert exact extracted inventory.'
    }
    $manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $validatedSourceSha = $manifest.sourceSha.ToLowerInvariant()
    $manifestVerified = $true
    Write-Host "Verified complete P1 payload against manifest SHA-256: $manifestSha256"
} elseif ($Mode -eq 'IL2CPP') {
    Write-Warning 'P1 IL2CPP payload hashes are UNVERIFIED (no -ManifestFile / -ExpectedSourceSha); not source-bound acceptance.'
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
