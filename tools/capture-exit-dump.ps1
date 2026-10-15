# Optional native Windows snapshot helper. Loads once BEFORE Player launch.
# No external packages, no forced quit, and no full-process-memory flags.
[CmdletBinding()]
param(
    [switch]$LoadOnly,
    [ValidateRange(0,2147483647)][int]$TargetPid = 0,
    [string]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'

# Compile the P/Invoke class once at launcher startup, so expensive PowerShell
# process startup or Add-Type compilation never consumes the quit window.
if (-not ('VcrNativeShutdownDump' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public static class VcrNativeShutdownDump {
    [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump",
               SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess, uint processId, SafeFileHandle hFile, uint dumpType,
        IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

    public static Task CaptureAsync(int pid, string destination) {
        // Capture runs on a CLR worker. The observer's shutdown sampling loop
        // remains active and does not block on MiniDumpWriteDump.
        return Task.Factory.StartNew(() => Capture(pid, destination));
    }

    public static void Capture(int pid, string destination) {
        string partial = destination + ".partial";
        try {
            using (Process process = Process.GetProcessById(pid))
            using (FileStream file = new FileStream(partial,
                             FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                // MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules.
                // Normal minidumps contain thread stack memory; no full memory.
                const uint flags = 0x00001000 | 0x00000020;
                if (!MiniDumpWriteDump(process.Handle, (uint)pid,
                                       file.SafeFileHandle, flags,
                                       IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                                            "MiniDumpWriteDump failed");
                file.Flush();
                if (file.Length < 4096)
                    throw new InvalidOperationException("Incomplete native minidump");
            }
            File.Move(partial, destination);
        } finally {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }
}
'@ -ErrorAction Stop
}
if (-not $LoadOnly) {
    if ($TargetPid -le 0 -or [string]::IsNullOrWhiteSpace($OutputPath)) {
        throw 'Specify -LoadOnly or both -TargetPid and -OutputPath.'
    }
    [VcrNativeShutdownDump]::Capture($TargetPid, [IO.Path]::GetFullPath($OutputPath))
    Write-Output "Native thread/module dump captured: $OutputPath"
}
