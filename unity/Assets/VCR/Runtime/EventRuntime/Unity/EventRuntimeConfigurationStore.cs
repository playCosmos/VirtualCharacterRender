using System;
using System.IO;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.EventRuntime.Unity
{
    public sealed class EventRuntimeConfigurationStore
    {
        public const int CurrentVersion = 1;

        private const long MaxFileBytes =
            16L * 1024L * 1024L;

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
                System.IO.Path.GetFullPath(
                    path);
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
                if (!BoundedTextFile.TryReadUtf8(
                        _path,
                        MaxFileBytes,
                        out var json,
                        out var readError))
                {
                    error =
                        "Event runtime configuration load failed: " +
                        readError;
                    return false;
                }

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

            var stagedRules =
                rules == null
                    ? Array.Empty<EventRuntimeRule>()
                    : (EventRuntimeRule[])
                        rules.Clone();

            if (!EventRuntimeRuleSetBounds
                .TryValidate(
                    stagedRules,
                    out error))
            {
                return false;
            }

            try
            {
                var envelope =
                    new EventRuntimeConfigurationEnvelope
                    {
                        Version =
                            CurrentVersion,
                        MaxCommandsPerEvent =
                            ClampMaxCommands(
                                maxCommandsPerEvent),
                        Rules =
                            stagedRules
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
                        "Event runtime configuration save failed: " +
                        writeError;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
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
                    var stagedRules =
                        envelope.Rules ??
                        Array.Empty<EventRuntimeRule>();

                    if (!EventRuntimeRuleSetBounds
                        .TryValidate(
                            stagedRules,
                            out error))
                    {
                        return false;
                    }

                    rules =
                        stagedRules;
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

    }
}
