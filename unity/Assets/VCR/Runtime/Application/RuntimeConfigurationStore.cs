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

        public bool TryLoad(
            out SceneRuntimeConfiguration configuration,
            out string error)
        {
            configuration =
                SceneRuntimeConfiguration.Default;
            error = null;

            if (!File.Exists(_path))
            {
                return true;
            }

            try
            {
                if (!BoundedTextFile.TryReadUtf8(
                        _path,
                        MaxFileBytes,
                        out var json,
                        out var readError))
                {
                    error =
                        "Configuration load failed: " +
                        readError;
                    return false;
                }

                var envelope =
                    JsonUtility.FromJson<
                        RuntimeConfigurationEnvelope>(
                        json);

                if (envelope == null)
                {
                    error =
                        "Configuration JSON did not produce a valid envelope.";
                    return false;
                }

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
                        out var writeError))
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
