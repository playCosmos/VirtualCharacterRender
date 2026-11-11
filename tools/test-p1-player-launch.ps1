# Windows-only native launcher contract test; the mock is NOT a real Unity Player.
# It prevents the operator script from silently dropping paths, exit codes,
# evidence or IL2CPP artifact checks before physical acceptance.
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('vcr-p1-launch-test-' + [guid]::NewGuid().ToString('N'))
$player = Join-Path $root 'P1 release with spaces'
$evidence = Join-Path $root 'Evidence with spaces'
$vrm = Join-Path $root 'model with spaces.vrm'
$config = Join-Path $root 'config with spaces.json'
$argsFile = Join-Path $root 'mock arguments.txt'
$launcher = Join-Path $PSScriptRoot 'run-p1-player.ps1'

New-Item -ItemType Directory -Path $player -Force | Out-Null
try {
    $exe = Join-Path $player 'VirtualCharacterRender.exe'
    $code = @'
using System;
using System.IO;
class MockP1Player {
    public static int Main(string[] args) {
        string sink = Environment.GetEnvironmentVariable("VCR_P1_MOCK_ARGUMENTS");
        if (String.IsNullOrEmpty(sink)) return 80;
        File.WriteAllLines(sink, args);
        int index = Array.IndexOf(args, "-logFile");
        if (index < 0 || index + 1 == args.Length) return 81;
        File.WriteAllText(args[index + 1], "Mock test Player - NOT Unity evidence\n");
        return 17;
    }
}
'@
    Add-Type -TypeDefinition $code -OutputAssembly $exe -OutputType ConsoleApplication
    Set-Content -LiteralPath $vrm -Value 'mock vrm' -Encoding UTF8
    Set-Content -LiteralPath $config -Value '{}' -Encoding UTF8
    $env:VCR_P1_MOCK_ARGUMENTS = $argsFile

    # Missing IL2CPP binaries must block launch before the mock is reached.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Mode IL2CPP -PlayerDirectory $player -EvidenceDirectory $evidence
    if ($LASTEXITCODE -eq 17 -or (Test-Path -LiteralPath $argsFile)) {
        throw 'IL2CPP launcher allowed an incomplete Player'
    }

    foreach ($relative in @(
        'GameAssembly.dll',
        'UnityPlayer.dll',
        'VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat'
    )) {
        $target = Join-Path $player $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Set-Content -LiteralPath $target -Value 'mock binary marker' -Encoding UTF8
    }
    New-Item -ItemType Directory -Path (Join-Path $player 'MonoBleedingEdge') | Out-Null
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Mode IL2CPP -PlayerDirectory $player -EvidenceDirectory $evidence
    if ($LASTEXITCODE -eq 17 -or (Test-Path -LiteralPath $argsFile)) {
        throw 'IL2CPP launcher allowed the Mono runtime'
    }
    Remove-Item -LiteralPath (Join-Path $player 'MonoBleedingEdge') -Force -Recurse

    # A missing or directory-valued -Config must fail before process start.
    # Otherwise a manual test could silently run with default configuration.
    $missingConfig = Join-Path $root 'missing config.json'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Mode IL2CPP -PlayerDirectory $player -EvidenceDirectory $evidence `
        -Config $missingConfig
    if ($LASTEXITCODE -eq 17 -or (Test-Path -LiteralPath $argsFile)) {
        throw 'P1 launcher started despite a missing config file'
    }
    $configDirectory = Join-Path $root 'directory is not a config.json'
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Mode IL2CPP -PlayerDirectory $player -EvidenceDirectory $evidence `
        -Config $configDirectory
    if ($LASTEXITCODE -eq 17 -or (Test-Path -LiteralPath $argsFile)) {
        throw 'P1 launcher started despite a directory-valued config'
    }

    # The mock returns 17. The launcher must wait for it and preserve 17,
    # instead of treating GUI launch as a successful asynchronous spawn.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Mode IL2CPP -PlayerDirectory $player -EvidenceDirectory $evidence `
        -Vrm $vrm -Config $config
    if ($LASTEXITCODE -ne 17) {
        throw "Expected native mock exit 17; got $LASTEXITCODE"
    }

    $actualArgs = @(Get-Content -LiteralPath $argsFile)
    # Windows PowerShell can use the 8.3 path RUNNER~1 in the child
    # process even when the parent resolves the same file to runneradmin.
    # Compare the actual target file and hash, not string spellings.
    $vrmArgs = @($actualArgs | Where-Object { $_ -like '--vcr-vrm=*' })
    $forwardedVrm = if ($vrmArgs.Count -eq 1) {
        $vrmArgs[0].Substring('--vcr-vrm='.Length)
    } else { '' }
    # Keep failures actionable: a named assertion distinguishes an actual
    # Win32 argument-quoting bug from short/long-path equivalence.
    $argumentFailures = @()
    if ($actualArgs.Count -ne 4) { $argumentFailures += 'argument-count' }
    if ($vrmArgs.Count -ne 1) { $argumentFailures += 'vrm-argument-count' }
    if (-not (Test-Path -LiteralPath $forwardedVrm -PathType Leaf)) {
        $argumentFailures += 'vrm-file-missing'
    } elseif ((Get-FileHash -LiteralPath $forwardedVrm -Algorithm SHA256).Hash -ne
              (Get-FileHash -LiteralPath $vrm -Algorithm SHA256).Hash) {
        $argumentFailures += 'vrm-file-hash'
    }
    $configArgs = @($actualArgs | Where-Object { $_ -like '--vcr-config=*' })
    $forwardedConfig = if ($configArgs.Count -eq 1) {
        $configArgs[0].Substring('--vcr-config='.Length)
    } else { '' }
    if ($configArgs.Count -ne 1 -or
        -not (Test-Path -LiteralPath $forwardedConfig -PathType Leaf)) {
        $argumentFailures += 'config-argument'
    } elseif ((Get-FileHash -LiteralPath $forwardedConfig -Algorithm SHA256).Hash -ne
              (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash) {
        $argumentFailures += 'config-file-hash'
    }

    $logOptionIndex = [Array]::IndexOf([string[]]$actualArgs, '-logFile')
    $forwardedLog = if ($logOptionIndex -ge 0 -and
                        $logOptionIndex + 1 -lt $actualArgs.Count) {
        $actualArgs[$logOptionIndex + 1]
    } else { '' }
    if ($logOptionIndex -lt 0) { $argumentFailures += 'log-option' }
    if (-not (Test-Path -LiteralPath $forwardedLog -PathType Leaf) -or
        [IO.Path]::GetFileName($forwardedLog) -ne 'Player.log') {
        $argumentFailures += 'log-path'
    } elseif ((Get-FileHash -LiteralPath $forwardedLog -Algorithm SHA256).Hash -ne
              (Get-FileHash -LiteralPath (Join-Path $evidence 'Player.log') -Algorithm SHA256).Hash) {
        $argumentFailures += 'log-file-hash'
    }
    if ($argumentFailures.Count -gt 0) {
        throw ("P1 GUI launcher argument failures: " +
            ($argumentFailures -join ', ') + "; arguments=" +
            ($actualArgs -join ' | '))
    }

    $record = Get-Content -LiteralPath (Join-Path $evidence 'launch-evidence.json') -Raw |
        ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if ($record.mode -ne 'IL2CPP' -or $record.exitCode -ne 17 -or
        $record.physicalHumanAcceptance -ne $false -or
        $record.playerExecutableSha256 -ne $hash -or
        -not (Test-Path -LiteralPath (Join-Path $evidence 'Player.log'))) {
        throw 'P1 physical operator launch evidence was incorrect'
    }
    if ((Get-Content -LiteralPath (Join-Path $evidence 'Player.log') -Raw) -notmatch
        'Mock test Player') {
        throw 'Mock Player log was not directed to the evidence folder'
    }
    Write-Host 'P1 Windows operator launcher mock: PASS (not physical UI acceptance)'
} finally {
    Remove-Item Env:\VCR_P1_MOCK_ARGUMENTS -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
}
# An expected mock exit code 17 must not leak into GitHub Actions' own exit.
# Every assertion above throws on failure; a completed test is success.
exit 0
