# Optional native Windows hang snapshot. Run only from the explicit -NativeDump collector mode.
# Captures thread stacks and module information, NOT full process memory.
# Microsoft Windows dbghelp.dll is an OS component; no third-party download.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$TargetPid,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$temporary = $OutputPath + '.partial'
try {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class VcrNativeShutdownDump {
    [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump",
               SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess, uint processId, SafeFileHandle hFile,
        uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam,
        IntPtr callbackParam);

    public static void Capture(int pid, string destination) {
        using (Process process = Process.GetProcessById(pid))
        using (FileStream file = new FileStream(destination, FileMode.CreateNew,
                                                FileAccess.Write, FileShare.None)) {
            // MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules.
            // Normal minidumps include thread stack memory; do NOT use
            // MiniDumpWithFullMemory, Heap or PrivateReadWriteMemory.
            const uint flags = 0x00001000 | 0x00000020;
            if (!MiniDumpWriteDump(process.Handle, (uint)pid,
                                   file.SafeFileHandle, flags,
                                   IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                                        "MiniDumpWriteDump failed");
            }
            file.Flush();
            if (file.Length < 4096)
                throw new InvalidOperationException("Native dump is incomplete");
        }
    }
}
'@ -ErrorAction Stop

    [VcrNativeShutdownDump]::Capture($TargetPid, $temporary)
    Move-Item -LiteralPath $temporary -Destination $OutputPath -ErrorAction Stop
    Write-Output "Thread/module dump captured: $OutputPath"
} catch {
    Write-Error -ErrorAction Continue ("Native thread dump failed: " +
        $_.Exception.ToString())
    exit 1
} finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}
