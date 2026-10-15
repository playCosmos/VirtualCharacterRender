# Built-in Windows PowerShell 5.1+ collector. No Python, service or external DLL.
# Run a new interactive Player, close it normally, then share the LOCAL ZIP
# after reviewing the possible paths/model names contained in Player.log.
[CmdletBinding()]
param(
    [string]$PlayerPath = (Join-Path $PSScriptRoot 'VirtualCharacterRender.exe'),
    [string]$OutputRoot = (Join-Path $env:LOCALAPPDATA 'VirtualCharacterRender\ExitDiagnostics'),
    [ValidateRange(10,3600)][int]$TimeoutSeconds = 180,
    [switch]$NativeDump,
    [ValidateRange(1,60)][int]$DumpDelaySeconds = 5
)
$ErrorActionPreference = 'Stop'
$PlayerPath = [IO.Path]::GetFullPath($PlayerPath)
if (-not [IO.File]::Exists($PlayerPath)) {
    throw "Player executable not found: $PlayerPath"
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
[void][IO.Directory]::CreateDirectory($OutputRoot)
$id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + ([guid]::NewGuid().ToString('N').Substring(0,8))
$session = Join-Path $OutputRoot $id
[void][IO.Directory]::CreateDirectory($session)
$log = Join-Path $session 'Player.log'
$launchedAt = Get-Date
$events = [ordered]@{
    schema = 'vcr-real-pc-exit-v1'
    launchedAt = $launchedAt.ToString('o')
    executable = $PlayerPath
    timedOut = $false
    processExited = $false
    exitCode = $null
    quitRequestedAtMs = $null
    managedQuitFinishedAtMs = $null
    lateManagedQuitAtMs = $null
    windowsGoneAtMs = $null
    quitToProcessExitMs = $null
    quitMarkerObservedAfterExit = $false
    nativeDumpRequested = [bool]$NativeDump
    nativeDumpStatus = 'not-requested'
    nativeDumpBytes = $null
    threadSampleCount = 0
}
$script:offset = [long]0
$script:carry = ''
$watch = [Diagnostics.Stopwatch]::StartNew()
$threadSamples = Join-Path $session 'thread-states.jsonl'
$dumpDestination = Join-Path $session 'native-exit-threads.dmp'
$dumpTask = $null
$dumpStarted = $false
$sampled = @{}

function Record-ThreadSample {
    param([int]$CheckpointSeconds)
    # Observe all target process threads; no thread is suspended or modified.
    try {
        $target = Get-Process -Id $process.Id -ErrorAction Stop
        $threads = @()
        foreach ($thread in $target.Threads) {
            try {
                $state = [string]$thread.ThreadState
                $wait = if ($state -eq 'Wait') { [string]$thread.WaitReason } else { $null }
                $threads += [ordered]@{
                    id = $thread.Id
                    state = $state
                    waitReason = $wait
                    cpuMs = [math]::Round($thread.TotalProcessorTime.TotalMilliseconds, 1)
                    startAddress = ('0x{0:X}' -f $thread.StartAddress.ToInt64())
                }
            } catch {
                $threads += [ordered]@{ id = $thread.Id; observationError = $_.Exception.Message }
            }
        }
        $snapshot = [ordered]@{
            elapsedMs = [long]$watch.ElapsedMilliseconds
            sinceQuitMs = [long]($watch.ElapsedMilliseconds - $events.quitRequestedAtMs)
            checkpointSeconds = $CheckpointSeconds
            totalProcessCpuMs = [math]::Round($target.TotalProcessorTime.TotalMilliseconds, 1)
            processThreadCount = $threads.Count
            threads = $threads
        }
        [IO.File]::AppendAllText($threadSamples,
            (($snapshot | ConvertTo-Json -Depth 6 -Compress) + "`n"),
            [Text.Encoding]::UTF8)
        $events.threadSampleCount = [int]$events.threadSampleCount + 1
    } catch {
        [IO.File]::AppendAllText($threadSamples,
            ('{"checkpointSeconds":' + $CheckpointSeconds +
             ',"error":"' + $_.Exception.GetType().Name + '"}' + "`n"),
            [Text.Encoding]::UTF8)
    }
}

function Start-NativeDump {
    if (-not $NativeDump -or $dumpStarted) { return }
    $script:dumpStarted = $true
    $events.nativeDumpTriggeredAtMs = [long]$watch.ElapsedMilliseconds
    if ($events.nativeDumpStatus -ne 'ready') { return }
    try {
        # Native helper was compiled before launching the GUI Player.
        # This CLR worker captures the stack while this PowerShell observer
        # continues polling windows and timestamps without blocking.
        $script:dumpTask = [VcrNativeShutdownDump]::CaptureAsync(
            $process.Id, $dumpDestination)
        $events.nativeDumpStatus = 'running'
    } catch {
        $events.nativeDumpStatus = 'launch-failed'
        $events.nativeDumpError = $_.Exception.ToString()
    }
}


# All native calls below only OBSERVE window state.
$nativeWindowCount = $null
try {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class VcrExitWindowProbe {
    delegate bool Visitor(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visitor cb, IntPtr state);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder name, int max);
    public static int Count(int pid) {
        int count = 0;
        EnumWindows((hwnd, state) => {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != (uint)pid || !IsWindowVisible(hwnd)) return true;
            var cls = new StringBuilder(128);
            var title = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            GetWindowText(hwnd, title, title.Capacity);
            if (cls.ToString().Contains("UnityWndClass") ||
                cls.ToString().StartsWith("VCR.NativeSettings.") ||
                title.ToString().Contains("VirtualCharacterRender")) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }
}
'@ -ErrorAction Stop
    $nativeWindowCount = { param([int]$pidValue) [VcrExitWindowProbe]::Count($pidValue) }
} catch {
    Write-Warning "Win32 window observation unavailable: $($_.Exception.Message)"
}

function Read-VcrLog {
    param([bool]$AfterExit = $false)
    if (-not [IO.File]::Exists($log)) { return }
    try {
        $stream = [IO.File]::Open($log, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            if ($stream.Length -lt $script:offset) {
                $script:offset = 0
                $script:carry = ''
            }
            [void]$stream.Seek($script:offset, [IO.SeekOrigin]::Begin)
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
            $incoming = $reader.ReadToEnd()
            $script:offset = $stream.Position
            # Do not dispose the reader separately: it owns the same stream.
        } finally {
            $stream.Dispose()
        }
        if ($incoming.Length -eq 0) { return }
        $combined = $script:carry + $incoming
        $now = [long]$watch.ElapsedMilliseconds
        if ($null -eq $events.quitRequestedAtMs -and $combined.Contains('VCR exit: quit requested (t=0 ms)')) {
            $events.quitRequestedAtMs = $now
            $events.quitMarkerObservedAfterExit = $AfterExit
        }
        if ($null -eq $events.managedQuitFinishedAtMs -and $combined.Contains('VCR exit: OnApplicationQuit finished at ')) {
            $events.managedQuitFinishedAtMs = $now
        }
        if ($null -eq $events.lateManagedQuitAtMs -and $combined.Contains('VCR exit: late managed quit callback at ')) {
            $events.lateManagedQuitAtMs = $now
        }
        $script:carry = $combined.Substring([Math]::Max(0, $combined.Length - 120))
    } catch [IO.IOException] {
        # A crash or exclusive log handle must not abort the evidence capture.
    }
}

if ($NativeDump) {
    $helper = Join-Path $PSScriptRoot 'capture-exit-dump.ps1'
    try {
        if (-not (Test-Path -LiteralPath $helper)) {
            throw "Native capture helper missing: $helper"
        }
        # Compilation is completed BEFORE process launch / timed quit path.
        . $helper -LoadOnly
        $events.nativeDumpStatus = 'ready'
    } catch {
        $events.nativeDumpStatus = 'preparation-failed'
        $events.nativeDumpError = $_.Exception.ToString()
    }
}

Write-Host "Starting GUI Player: $PlayerPath"
Write-Host 'Close the Player normally; do not force quit or minimize it.'
Write-Host "Evidence directory: $session"
$process = $null
try {
    $launchedAt = Get-Date
    $events.launchedAt = $launchedAt.ToString('o')
    $watch.Restart()
    $process = Start-Process -FilePath $PlayerPath -WorkingDirectory (
        [IO.Path]::GetDirectoryName($PlayerPath)) -ArgumentList @(
        '-logFile', ('"' + $log + '"')) -PassThru
    $events['pid'] = $process.Id
    $everVisible = $false
    while (-not $process.HasExited) {
        Read-VcrLog
        if ($null -ne $events.quitRequestedAtMs -and
            -not $events.quitMarkerObservedAfterExit) {
            $elapsedSinceQuit = ($watch.ElapsedMilliseconds - $events.quitRequestedAtMs) / 1000.0
            foreach ($checkpoint in @(1, 5, 10, 15)) {
                if ($elapsedSinceQuit -ge $checkpoint -and
                    -not $sampled.ContainsKey($checkpoint)) {
                    $sampled[$checkpoint] = $true
                    Record-ThreadSample -CheckpointSeconds $checkpoint
                }
            }
            if ($NativeDump -and -not $dumpStarted -and
                $elapsedSinceQuit -ge $DumpDelaySeconds) {
                Start-NativeDump
            }
        }
        if ($null -ne $nativeWindowCount) {
            try {
                $visible = & $nativeWindowCount $process.Id
                if ($visible -gt 0) {
                    $everVisible = $true
                } elseif ($everVisible -and $null -eq $events.windowsGoneAtMs) {
                    $events.windowsGoneAtMs = [long]$watch.ElapsedMilliseconds
                }
            } catch { }
        }
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $events.timedOut = $true
            Write-Warning 'Observation timed out. The Player is NOT killed.'
            break
        }
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    }
    Read-VcrLog -AfterExit ($process.HasExited)
    # The last 100ms poll may miss the exact window-disappearance event
    # because the process has already exited. This final read-only probe
    # records that it is gone, without pretending to know an earlier time.
    if ($everVisible -and $null -eq $events.windowsGoneAtMs -and
        $null -ne $nativeWindowCount) {
        try {
            $visible = & $nativeWindowCount $process.Id
            if ($visible -eq 0) {
                $events.windowsGoneAtMs = [long]$watch.ElapsedMilliseconds
                $events['windowsGoneObservedAfterExit'] = [bool]$process.HasExited
            }
        } catch { }
    }
    $events.processExited = [bool]$process.HasExited
    if ($process.HasExited) {
        $events.exitCode = $process.ExitCode
    }
} catch {
    $events['observerError'] = $_.Exception.ToString()
} finally {
    if ($null -ne $dumpTask) {
        try {
            if (-not $dumpTask.Wait(15000)) {
                $events.nativeDumpStatus = 'still-writing'
            }
        } catch {
            $events.nativeDumpStatus = 'failed'
            $events.nativeDumpError = $_.Exception.ToString()
            $events.nativeDumpError |
                Set-Content -Path (Join-Path $session 'native-dump-worker.txt') -Encoding UTF8
        }
    }
    if ($NativeDump -and $dumpStarted) {
        if ((Test-Path -LiteralPath $dumpDestination) -and
            (Get-Item -LiteralPath $dumpDestination).Length -gt 4096) {
            $events.nativeDumpStatus = 'captured'
            $events.nativeDumpBytes = (Get-Item -LiteralPath $dumpDestination).Length
        } elseif ($events.nativeDumpStatus -eq 'running') {
            $events.nativeDumpStatus = 'failed'
        }
    } elseif ($NativeDump -and -not $dumpStarted -and
              $events.nativeDumpStatus -eq 'ready') {
        $events.nativeDumpStatus = 'no-slow-quit-observed'
    }
    $events['observationEndedAt'] = (Get-Date).ToString('o')
    $events['observationDurationMs'] = [long]$watch.ElapsedMilliseconds
    if ($null -ne $events.quitRequestedAtMs -and $events.processExited -and
        -not $events.quitMarkerObservedAfterExit) {
        $events.quitToProcessExitMs = (
            $events.observationDurationMs - $events.quitRequestedAtMs)
    }
    if ([IO.File]::Exists($log)) {
        Select-String -Path $log -Pattern 'VCR exit:', 'VCR shutdown:', 'VCR shutdown step',
            'VCR MediaPipe cleanup', 'VCR UniWinC shutdown' -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Line } |
            Set-Content -Path (Join-Path $session 'shutdown-events.txt') -Encoding UTF8
    }
    $events | ConvertTo-Json -Depth 6 |
        Set-Content -Path (Join-Path $session 'exit-diagnosis.json') -Encoding UTF8

    try {
        $system = @()
        $system += "Windows: $([Environment]::OSVersion.VersionString)"
        $system += "PowerShell: $($PSVersionTable.PSVersion)"
        $system += "Process architecture: $([Environment]::Is64BitProcess)"
        $gpus = Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop
        foreach ($gpu in $gpus) {
            $system += "GPU: $($gpu.Name), driver $($gpu.DriverVersion)"
        }
        $system | Set-Content -Path (Join-Path $session 'system-summary.txt') -Encoding UTF8
    } catch {
        "Hardware summary unavailable: $($_.Exception.Message)" |
            Set-Content (Join-Path $session 'system-summary.txt')
    }
    try {
        Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$launchedAt} -ErrorAction Stop |
            Where-Object { $_.ProviderName -in @('Application Error', 'Windows Error Reporting') -and
                $_.Message -match 'VirtualCharacterRender|UnityPlayer' } |
            Select-Object TimeCreated, ProviderName, Id, Message |
            Format-List | Out-File -FilePath (Join-Path $session 'application-errors.txt') -Encoding UTF8
    } catch { }

    $zip = Join-Path $OutputRoot ("exit-diagnostics-" + $id + ".zip")
    Compress-Archive -Path (Join-Path $session '*') -DestinationPath $zip -Force
    Write-Host "Exit diagnostics saved: $zip"
    Write-Host 'Review the ZIP for local paths and model names before sharing.'
    if ($NativeDump) {
        Write-Host 'The native .dmp may contain passwords, file paths and private process data.'
        Write-Host 'Do NOT share it publicly; inspect/restrict sharing separately.'
    }
}
