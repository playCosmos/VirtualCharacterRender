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
                var directory =
                    System.IO.Path.GetDirectoryName(
                        _path);

                if (!string.IsNullOrWhiteSpace(
                        directory))
                {
                    Directory.CreateDirectory(
                        directory);
                }

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

                var temporaryPath =
                    _path + ".tmp";
                var backupPath =
                    _path + ".bak";

                if (!BoundedTextFile.TryValidateUtf8Size(
                        json,
                        MaxFileBytes,
                        out var sizeError))
                {
                    error =
                        "Configuration save failed: " +
                        sizeError;
                    return false;
                }

                File.WriteAllText(
                    temporaryPath,
                    json);

                if (File.Exists(_path))
                {
                    File.Replace(
                        temporaryPath,
                        _path,
                        backupPath);

                    if (File.Exists(
                            backupPath))
                    {
                        File.Delete(
                            backupPath);
                    }
                }
                else
                {
                    File.Move(
                        temporaryPath,
                        _path);
                }

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    var temporaryPath =
                        _path + ".tmp";

                    if (File.Exists(
                            temporaryPath))
                    {
                        File.Delete(
                            temporaryPath);
                    }
                }
                catch
                {
                    // Keep the original save error.
                }

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
