using System;
using System.IO;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Scene;

namespace VCR.Runtime.Application
{
    public sealed class RuntimeConfigurationStore
    {
        public const int CurrentVersion = 1;

        private const long MaxFileBytes =
            4L * 1024L * 1024L;

        private readonly string _path;

        public RuntimeConfigurationStore(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Configuration path is required.",
                    nameof(path));
            }

            _path = System.IO.Path.GetFullPath(path);
        }

        public string Path => _path;

        public string LastLoadWarning { get; private set; }

        public bool TryLoad(
            out SceneRuntimeConfiguration configuration,
            out string error)
        {
            LastLoadWarning = null;
            configuration = SceneRuntimeConfiguration.Default;
            error = null;

            if (!File.Exists(_path))
            {
                return true;
            }

            if (TryLoadDocument(
                    _path,
                    out configuration,
                    out error,
                    out var newerVersion))
            {
                return true;
            }

            // A future-version configuration must remain untouched. Do not
            // silently downgrade it by loading an old backup.
            if (newerVersion || !File.Exists(_path + ".bak"))
            {
                return false;
            }

            var primaryError = error;
            if (!TryLoadDocument(
                    _path + ".bak",
                    out configuration,
                    out var backupError,
                    out _))
            {
                error = primaryError +
                    " Previous backup also failed: " + backupError;
                return false;
            }

            // Preserve the unreadable primary verbatim for diagnosis;
            // the next explicit save replaces it via the normal atomic path.
            LastLoadWarning =
                "Configuration was recovered from the previous .bak file " +
                "because the current file could not be loaded. " +
                "The unreadable original was not automatically overwritten.";
            Debug.LogWarning("VCR: " + LastLoadWarning);
            error = null;
            return true;
        }

        private static bool TryLoadDocument(
            string path,
            out SceneRuntimeConfiguration configuration,
            out string error,
            out bool newerVersion)
        {
            configuration = SceneRuntimeConfiguration.Default;
            error = null;
            newerVersion = false;

            try
            {
                if (!BoundedTextFile.TryReadUtf8(
                        path,
                        MaxFileBytes,
                        out var json,
                        out var readError))
                {
                    error = "Configuration load failed: " + readError;
                    return false;
                }

                var envelope =
                    JsonUtility.FromJson<RuntimeConfigurationEnvelope>(json);
                if (envelope == null)
                {
                    error =
                        "Configuration JSON did not produce a valid envelope.";
                    return false;
                }

                newerVersion = envelope.Version > CurrentVersion;
                return TryMigrate(
                    envelope,
                    out configuration,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    "Configuration load failed: " +
                    exception.Message;
                return false;
            }
        }

        public bool TrySave(
            SceneRuntimeConfiguration configuration,
            out string error)
        {
            error = null;

            try
            {
                var envelope =
                    new RuntimeConfigurationEnvelope
                    {
                        Version =
                            CurrentVersion,
                        Scene =
                            configuration
                    };

                var json =
                    JsonUtility.ToJson(
                        envelope,
                        prettyPrint: true);

                if (!BoundedTextFile.TryWriteUtf8Atomic(
                        _path,
                        json,
                        MaxFileBytes,
                        out var writeError,
                        preserveBackup: true))
                {
                    error =
                        "Configuration save failed: " +
                        writeError;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Configuration save failed: " +
                    exception.Message;
                return false;
            }
        }

        private static bool TryMigrate(
            RuntimeConfigurationEnvelope envelope,
            out SceneRuntimeConfiguration configuration,
            out string error)
        {
            configuration =
                SceneRuntimeConfiguration.Default;
            error = null;

            switch (envelope.Version)
            {
                case CurrentVersion:
                    configuration =
                        envelope.Scene;
                    return true;

                default:
                    error =
                        envelope.Version >
                        CurrentVersion
                            ? $"Configuration version {envelope.Version} is newer than supported version {CurrentVersion}."
                            : $"Configuration version {envelope.Version} is unsupported and has no migration path.";
                    return false;
            }
        }
    }
}
