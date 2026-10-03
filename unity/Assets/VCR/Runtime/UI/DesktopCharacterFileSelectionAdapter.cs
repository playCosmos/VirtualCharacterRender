using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System.Diagnostics;
#endif

namespace VCR.Runtime.UI
{
    [DisallowMultipleComponent]
    public sealed class DesktopCharacterFileSelectionAdapter :
        MonoBehaviour,
        ICharacterFileSelectionAdapter
    {
        public bool IsSupported
        {
            get
            {
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public string AdapterId
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return "desktop.windows.openfile";
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
                return "desktop.macos.choose-file";
#else
                return "desktop.unsupported";
#endif
            }
        }

        public string UnavailableReason =>
            IsSupported
                ? null
                : "Desktop file selection is available in Windows/macOS standalone builds. Unity Editor uses the P11 editor adapter.";

        public CharacterFileSelectionResult
            SelectCharacterFile(
                string currentPath)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return SelectWindows(
                currentPath);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return SelectMacOs(
                currentPath);
#else
            return CharacterFileSelectionResult
                .Failure(
                    UnavailableReason);
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrFilter;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public StringBuilder lpstrFile;
            public int nMaxFile;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrInitialDir;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        private const int OfnExplorer =
            0x00080000;
        private const int OfnFileMustExist =
            0x00001000;
        private const int OfnPathMustExist =
            0x00000800;
        private const int OfnNoChangeDir =
            0x00000008;

        [DllImport(
            "comdlg32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName(
            ref OpenFileName ofn);

        [DllImport("comdlg32.dll")]
        private static extern int
            CommDlgExtendedError();

        private static CharacterFileSelectionResult
            SelectWindows(
                string currentPath)
        {
            try
            {
                var initialDirectory =
                    ResolveInitialDirectory(
                        currentPath);
                var buffer =
                    new StringBuilder(
                        32768);

                var ofn =
                    new OpenFileName
                    {
                        lStructSize =
                            Marshal.SizeOf<
                                OpenFileName>(),
                        lpstrFilter =
                            "VRM Character (*.vrm)\0*.vrm\0All Files (*.*)\0*.*\0\0",
                        lpstrFile =
                            buffer,
                        nMaxFile =
                            buffer.Capacity,
                        lpstrInitialDir =
                            initialDirectory,
                        lpstrTitle =
                            "Select VRM Character",
                        Flags =
                            OfnExplorer |
                            OfnFileMustExist |
                            OfnPathMustExist |
                            OfnNoChangeDir,
                        lpstrDefExt =
                            "vrm"
                    };

                if (!GetOpenFileName(
                        ref ofn))
                {
                    var code =
                        CommDlgExtendedError();

                    return code == 0
                        ? CharacterFileSelectionResult
                            .CancelledResult()
                        : CharacterFileSelectionResult
                            .Failure(
                                $"Windows file dialog failed with common-dialog error 0x{code:X}.");
                }

                return ValidateSelectedPath(
                    buffer.ToString());
            }
            catch (Exception exception)
            {
                return CharacterFileSelectionResult
                    .Failure(
                        "Windows file dialog failed: " +
                        exception.Message);
            }
        }
#endif

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private static CharacterFileSelectionResult
            SelectMacOs(
                string currentPath)
        {
            var scriptPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "vcr-character-picker-" +
                    Guid.NewGuid()
                        .ToString("N") +
                    ".applescript");

            try
            {
                var initialDirectory =
                    ResolveInitialDirectory(
                        currentPath);
                var script =
                    new StringBuilder();

                if (!string.IsNullOrWhiteSpace(
                        initialDirectory))
                {
                    script.AppendLine(
                        "set initialFolder to POSIX file " +
                        AppleScriptQuote(
                            initialDirectory));
                    script.AppendLine(
                        "set chosenFile to choose file with prompt \"Select VRM Character\" default location initialFolder");
                }
                else
                {
                    script.AppendLine(
                        "set chosenFile to choose file with prompt \"Select VRM Character\"");
                }

                script.AppendLine(
                    "return POSIX path of chosenFile");

                File.WriteAllText(
                    scriptPath,
                    script.ToString());

                var startInfo =
                    new ProcessStartInfo
                    {
                        FileName =
                            "/usr/bin/osascript",
                        Arguments =
                            QuoteProcessArgument(
                                scriptPath),
                        UseShellExecute =
                            false,
                        RedirectStandardOutput =
                            true,
                        RedirectStandardError =
                            true,
                        CreateNoWindow =
                            true
                    };

                using var process =
                    Process.Start(
                        startInfo);

                if (process == null)
                {
                    return CharacterFileSelectionResult
                        .Failure(
                            "macOS file dialog process could not be started.");
                }

                var output =
                    process.StandardOutput
                        .ReadToEnd()
                        .Trim();
                var error =
                    process.StandardError
                        .ReadToEnd()
                        .Trim();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    if (error.IndexOf(
                            "-128",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        error.IndexOf(
                            "User canceled",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return CharacterFileSelectionResult
                            .CancelledResult();
                    }

                    return CharacterFileSelectionResult
                        .Failure(
                            "macOS file dialog failed: " +
                            (string.IsNullOrWhiteSpace(
                                 error)
                                ? $"osascript exit code {process.ExitCode}."
                                : error));
                }

                return ValidateSelectedPath(
                    output);
            }
            catch (Exception exception)
            {
                return CharacterFileSelectionResult
                    .Failure(
                        "macOS file dialog failed: " +
                        exception.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(
                            scriptPath))
                    {
                        File.Delete(
                            scriptPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static string AppleScriptQuote(
            string value)
        {
            value ??=
                string.Empty;

            return "\"" +
                   value
                       .Replace(
                           "\\",
                           "\\\\")
                       .Replace(
                           "\"",
                           "\\\"") +
                   "\"";
        }

        private static string QuoteProcessArgument(
            string value)
        {
            value ??=
                string.Empty;

            return "\"" +
                   value.Replace(
                       "\"",
                       "\\\"") +
                   "\"";
        }
#endif

        private static CharacterFileSelectionResult
            ValidateSelectedPath(
                string path)
        {
            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return CharacterFileSelectionResult
                    .Failure(
                        "File dialog returned an empty path.");
            }

            var normalized =
                Path.GetFullPath(
                    path.Trim());

            if (!File.Exists(
                    normalized))
            {
                return CharacterFileSelectionResult
                    .Failure(
                        "Selected character file does not exist.");
            }

            if (!string.Equals(
                    Path.GetExtension(
                        normalized),
                    ".vrm",
                    StringComparison.OrdinalIgnoreCase))
            {
                return CharacterFileSelectionResult
                    .Failure(
                        "Selected character file must use the .vrm extension.");
            }

            return CharacterFileSelectionResult
                .Success(
                    normalized);
        }

        private static string ResolveInitialDirectory(
            string currentPath)
        {
            if (string.IsNullOrWhiteSpace(
                    currentPath))
            {
                return null;
            }

            try
            {
                if (Directory.Exists(
                        currentPath))
                {
                    return Path.GetFullPath(
                        currentPath);
                }

                var directory =
                    Path.GetDirectoryName(
                        currentPath);

                return !string.IsNullOrWhiteSpace(
                           directory) &&
                       Directory.Exists(
                           directory)
                    ? Path.GetFullPath(
                        directory)
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
