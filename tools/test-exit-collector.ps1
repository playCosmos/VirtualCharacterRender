# Windows-only zero-install end-to-end test for real-PC exit collector.
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ('vcr-exit-collector-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
try {
    $exe = Join-Path $root 'VirtualCharacterRender.exe'
    $src = @'
using System;
using System.IO;
using System.Threading;
class ExitTestPlayer {
    public static int Main(string[] args) {
        int i = Array.IndexOf(args, "-logFile");
        if (i < 0 || i + 1 >= args.Length) return 2;
        string log = args[i + 1];
        File.WriteAllText(log, "VCR exit: quit requested (t=0 ms)\n");
        Thread.Sleep(450);
        File.AppendAllText(log,
            "VCR shutdown: end at 6 ms, success=True\n" +
            "VCR exit: OnApplicationQuit finished at 7 ms\n" +
            "VCR exit: late managed quit callback at 9 ms\n");
        Thread.Sleep(250);
        return 0;
    }
}
'@
    Add-Type -TypeDefinition $src -OutputAssembly $exe -OutputType ConsoleApplication
    $collector = Join-Path $PSScriptRoot 'collect-exit-diagnostics.ps1'
    $output = Join-Path $root 'evidence'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $collector -PlayerPath $exe -OutputRoot $output -TimeoutSeconds 15
    if ($LASTEXITCODE -ne 0) {
        throw "Real-PC collector exit code $LASTEXITCODE"
    }
    $sessions = @(Get-ChildItem -Path $output -Directory)
    if ($sessions.Count -ne 1) {
        throw "Expected one evidence session, saw $($sessions.Count)"
    }
    $dataFile = Join-Path $sessions[0].FullName 'exit-diagnosis.json'
    $data = Get-Content -Path $dataFile -Raw | ConvertFrom-Json
    if ($data.processExited -ne $true -or $data.exitCode -ne 0 -or
        $data.timedOut -ne $false -or $null -eq $data.quitRequestedAtMs -or
        $null -eq $data.managedQuitFinishedAtMs -or
        $null -eq $data.lateManagedQuitAtMs) {
        throw "Incomplete normal desktop evidence: $(Get-Content $dataFile -Raw)"
    }
    if (-not (Test-Path (Join-Path $sessions[0].FullName 'Player.log'))) {
        throw 'Player.log not collected'
    }
    $archives = @(Get-ChildItem -Path $output -Filter '*.zip' -File)
    if ($archives.Count -ne 1 -or $archives[0].Length -le 0) {
        throw 'Expected one nonempty, local ZIP'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archives[0].FullName)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        if ('exit-diagnosis.json' -notin $names -or 'Player.log' -notin $names) {
            throw "Missing expected evidence files: $names"
        }
    } finally {
        $zip.Dispose()
    }
    Write-Host 'Real Windows exit collector end-to-end PASS'
} finally {
    if (Test-Path $root) {
        Remove-Item -Path $root -Force -Recurse -ErrorAction SilentlyContinue
    }
}
