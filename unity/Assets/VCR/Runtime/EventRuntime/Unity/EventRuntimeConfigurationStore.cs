using System;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.EventRuntime.Unity
{
    public sealed class EventRuntimeConfigurationStore
    {
        public const int CurrentVersion = 1;

        private readonly string _path;

        public EventRuntimeConfigurationStore(
            string path)
        {
            if (string.IsNullOrWhiteSpace(
                    path))
            {
                throw new ArgumentException(
                    "Event runtime configuration path is required.",
                    nameof(path));
            }

            _path =
                Path.GetFullPath(path);
        }

        public string Path => _path;

        public bool TryLoad(
            out EventRuntimeRule[] rules,
            out int maxCommandsPerEvent,
            out string error)
        {
            rules =
                Array.Empty<EventRuntimeRule>();
            maxCommandsPerEvent = 32;
            error = null;

            if (!File.Exists(_path))
            {
                return true;
            }

            try
            {
                var json =
                    File.ReadAllText(_path);

                var envelope =
                    JsonUtility.FromJson<
                        EventRuntimeConfigurationEnvelope>(
                        json);

                if (envelope == null)
                {
                    error =
                        "Event runtime configuration JSON did not produce a valid envelope.";
                    return false;
                }

                return TryMigrate(
                    envelope,
                    out rules,
                    out maxCommandsPerEvent,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    "Event runtime configuration load failed: " +
                    exception.Message;
                return false;
            }
        }

        public bool TrySave(
            EventRuntimeRule[] rules,
            int maxCommandsPerEvent,
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
                    new EventRuntimeConfigurationEnvelope
                    {
                        Version =
                            CurrentVersion,
                        MaxCommandsPerEvent =
                            ClampMaxCommands(
                                maxCommandsPerEvent),
                        Rules =
                            rules == null
                                ? Array.Empty<EventRuntimeRule>()
                                : (EventRuntimeRule[])
                                    rules.Clone()
                    };

                var json =
                    JsonUtility.ToJson(
                        envelope,
                        prettyPrint: true);

                var temporaryPath =
                    _path + ".tmp";
                var backupPath =
                    _path + ".bak";

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
                TryDeleteTemporary();
                error =
                    "Event runtime configuration save failed: " +
                    exception.Message;
                return false;
            }
        }

        private static bool TryMigrate(
            EventRuntimeConfigurationEnvelope envelope,
            out EventRuntimeRule[] rules,
            out int maxCommandsPerEvent,
            out string error)
        {
            rules =
                Array.Empty<EventRuntimeRule>();
            maxCommandsPerEvent = 32;
            error = null;

            switch (envelope.Version)
            {
                case CurrentVersion:
                    rules =
                        envelope.Rules ??
                        Array.Empty<EventRuntimeRule>();
                    maxCommandsPerEvent =
                        ClampMaxCommands(
                            envelope.MaxCommandsPerEvent);
                    return true;

                default:
                    error =
                        envelope.Version >
                        CurrentVersion
                            ? $"Event runtime configuration version {envelope.Version} is newer than supported version {CurrentVersion}."
                            : $"Event runtime configuration version {envelope.Version} is unsupported and has no migration path.";
                    return false;
            }
        }

        private static int ClampMaxCommands(
            int value)
        {
            return Math.Max(
                1,
                Math.Min(
                    256,
                    value));
        }

        private void TryDeleteTemporary()
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
        }
    }
}
