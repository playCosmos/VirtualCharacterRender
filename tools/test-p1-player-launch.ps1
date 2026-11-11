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

    # The mock returns 17. The launcher must wait for it and preserve 17,
    # instead of treating GUI launch as a successful asynchronous spawn.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Mode IL2CPP -PlayerDirectory $player -EvidenceDirectory $evidence `
        -Vrm $vrm -Config $config
    if ($LASTEXITCODE -ne 17) {
        throw "Expected native mock exit 17; got $LASTEXITCODE"
    }

    $actualArgs = @(Get-Content -LiteralPath $argsFile)
    # Resolve-Path can return the short-form user profile (RUNNER~1) on
    # Windows. Match the launcher's own resolved VRM path, not the caller's
    # syntactically different but identical absolute pathname.
    $resolvedVrm = (Resolve-Path -LiteralPath $vrm).Path
    if ($actualArgs.Count -ne 4 -or
        "--vcr-vrm=$resolvedVrm" -notin $actualArgs -or
        "--vcr-config=$config" -notin $actualArgs -or
        '-logFile' -notin $actualArgs -or
        (Join-Path $evidence 'Player.log') -notin $actualArgs) {
        throw "P1 GUI launcher lost quoted arguments: $($actualArgs -join ' | ')"
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
