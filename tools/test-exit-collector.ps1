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
        bool slow = Environment.GetEnvironmentVariable("VCR_EXIT_TEST_SLOW") == "1";
        Thread.Sleep(slow ? 3200 : 450);
        File.AppendAllText(log,
            "VCR shutdown: end at 6 ms, success=True\n" +
            "VCR exit: OnApplicationQuit finished at 7 ms\n" +
            "VCR exit: late managed quit callback at 9 ms\n");
        Thread.Sleep(slow ? 2800 : 250);
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
    if ($null -eq $data.processExitObservedAtMs -or
        $data.quitToProcessExitMs -ne
            ($data.processExitObservedAtMs - $data.quitRequestedAtMs) -or
        $data.observationDurationMs -lt $data.processExitObservedAtMs) {
        throw "Incorrect normal process-exit timing: $(Get-Content $dataFile -Raw)"
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
    # Reproduce a slow native exit and verify the optional user-consented
    # minidump. This uses only Windows dbghelp.dll on the hosted runner.
    $env:VCR_EXIT_TEST_SLOW = '1'
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $collector `
            -PlayerPath $exe -OutputRoot $output -TimeoutSeconds 15 `
            -NativeDump -DumpDelaySeconds 1
        if ($LASTEXITCODE -ne 0) {
            throw "Native dump collector exit code $LASTEXITCODE"
        }
    } finally {
        Remove-Item Env:\VCR_EXIT_TEST_SLOW -ErrorAction SilentlyContinue
    }
    $sessions = @(Get-ChildItem -Path $output -Directory | Sort-Object Name)
    if ($sessions.Count -ne 2) {
        throw "Expected two evidence sessions, saw $($sessions.Count)"
    }
    $nativeFolder = $sessions[1].FullName
    $nativeResult = Get-Content (Join-Path $nativeFolder 'exit-diagnosis.json') -Raw |
        ConvertFrom-Json
    $minidump = Join-Path $nativeFolder 'native-exit-threads.dmp'
    if ($nativeResult.processExited -ne $true -or
        $nativeResult.nativeDumpStatus -ne 'captured' -or
        $nativeResult.nativeDumpBytes -le 4096 -or
        $nativeResult.threadSampleCount -lt 1 -or
        -not (Test-Path $minidump) -or
        -not (Test-Path (Join-Path $nativeFolder 'thread-states.jsonl'))) {
        $workerPath = Join-Path $nativeFolder 'native-dump-worker.txt'
        $workerDetail = if (Test-Path $workerPath) {
            Get-Content $workerPath -Raw
        } else { 'No worker log recorded' }
        throw ("Native dump evidence missing: " +
            (Get-Content (Join-Path $nativeFolder 'exit-diagnosis.json') -Raw) +
            " worker: " + $workerDetail)
    }
    if ($null -eq $nativeResult.processExitObservedAtMs -or
        $nativeResult.quitToProcessExitMs -ne
            ($nativeResult.processExitObservedAtMs - $nativeResult.quitRequestedAtMs) -or
        $nativeResult.observationDurationMs -lt $nativeResult.processExitObservedAtMs) {
        throw "Dump finalization corrupted quit timing: $($nativeResult | ConvertTo-Json)"
    }
    $magic = [IO.File]::ReadAllBytes($minidump)
    if ([Text.Encoding]::ASCII.GetString($magic, 0, 4) -ne 'MDMP') {
        throw 'Expected Windows minidump MDMP header'
    }
    $nativeArchive = @(Get-ChildItem -Path $output -Filter '*.zip' -File |
        Sort-Object Name)[1]
    $zip2 = [IO.Compression.ZipFile]::OpenRead($nativeArchive.FullName)
    try {
        $names2 = @($zip2.Entries | ForEach-Object { $_.FullName })
        if ('native-exit-threads.dmp' -notin $names2 -or
            'thread-states.jsonl' -notin $names2) {
            throw "Missing native snapshot files in ZIP: $names2"
        }
    } finally {
        $zip2.Dispose()
    }
    # An unfinished native dump is held with FileShare.None. The evidence
    # collector must succeed without trying to ZIP that live .partial file.
    $lockedOutput = Join-Path $root 'locked-evidence'
    $stdOutPath = Join-Path $root 'locked-collector-stdout.txt'
    $stdErrPath = Join-Path $root 'locked-collector-stderr.txt'
    $collectorArgs = '-NoProfile -ExecutionPolicy Bypass -File "' + $collector +
        '" -PlayerPath "' + $exe + '" -OutputRoot "' + $lockedOutput +
        '" -TimeoutSeconds 15'
    $collectorProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList $collectorArgs `
        -PassThru -RedirectStandardOutput $stdOutPath -RedirectStandardError $stdErrPath
    $partialHandle = $null
    try {
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        $lockedSessions = @()
        while ($deadline.Elapsed.TotalSeconds -lt 10) {
            $lockedSessions = @(Get-ChildItem -LiteralPath $lockedOutput -Directory `
                -ErrorAction SilentlyContinue)
            if ($lockedSessions.Count -eq 1 -or $collectorProcess.HasExited) { break }
            Start-Sleep -Milliseconds 20
        }
        if ($lockedSessions.Count -ne 1) {
            throw 'Locked-partial collector did not create an evidence directory'
        }
        $partialPath = Join-Path $lockedSessions[0].FullName 'native-exit-threads.dmp.partial'
        $partialHandle = [IO.File]::Open($partialPath, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::None)
        $partialHandle.WriteByte(42)
        $partialHandle.Flush()
        if (-not $collectorProcess.WaitForExit(20000)) {
            throw 'Locked-partial collector timed out'
        }
        if ($collectorProcess.ExitCode -ne 0) {
            throw ('Locked-partial collector failed: ' +
                (Get-Content $stdErrPath -Raw))
        }
        $lockedArchives = @(Get-ChildItem -LiteralPath $lockedOutput -File -Filter '*.zip')
        if ($lockedArchives.Count -ne 1) {
            throw 'Expected one ZIP despite locked partial minidump'
        }
        $lockedZip = [IO.Compression.ZipFile]::OpenRead($lockedArchives[0].FullName)
        try {
            $lockedNames = @($lockedZip.Entries | ForEach-Object { $_.FullName })
            if ('native-exit-threads.dmp.partial' -in $lockedNames -or
                'exit-diagnosis.json' -notin $lockedNames -or
                'Player.log' -notin $lockedNames) {
                throw "Incomplete ZIP or partial dump archived: $lockedNames"
            }
        } finally {
            $lockedZip.Dispose()
        }
    } finally {
        if ($null -ne $partialHandle) { $partialHandle.Dispose() }
        if (-not $collectorProcess.HasExited) {
            $collectorProcess.Kill()
            $collectorProcess.WaitForExit()
        }
        $collectorProcess.Dispose()
    }
    Write-Host 'Real Windows exit collector, minidump and locked-partial ZIP PASS'
} finally {
    if (Test-Path $root) {
        Remove-Item -Path $root -Force -Recurse -ErrorAction SilentlyContinue
    }
}
