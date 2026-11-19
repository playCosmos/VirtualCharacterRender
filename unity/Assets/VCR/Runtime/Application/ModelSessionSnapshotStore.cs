using System;
using System.IO;
using System.Globalization;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Application
{
    // Recorded model state is evidence about a session, not the authority
    // for next-start restoration. PlayerPrefs v1 remains authoritative.
    [Serializable]
    public sealed class ModelSessionSnapshot
    {
        public int Version;
        public string RememberedFormat;
        public string RememberedPath;
        public string ActiveFormat;
        public string ActivePath;
        public string CaptureReason;
        public string CapturedUtc;
    }

    // Operator-triggered, versioned snapshot with bounded I/O and rollback
    // boundary. A damaged/future snapshot is never downgraded automatically.
    public sealed class ModelSessionSnapshotStore
    {
        public const int CurrentVersion = 1;
        public const int MaxPathLength = 4096;
        private const long MaxBytes = 32L * 1024L;
        private readonly string _path;

        public ModelSessionSnapshotStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Session snapshot path is required.",
                    nameof(filePath));
            _path = Path.GetFullPath(filePath);
        }

        public string FilePath => _path;

        public static ModelSessionSnapshotStore CreateDefault()
        {
            return new ModelSessionSnapshotStore(Path.Combine(
                UnityEngine.Application.persistentDataPath, "model-session.v1.json"));
        }

        // Missing primary+backup is an empty new profile (null snapshot).
        // Missing primary with an existing backup is an interrupted/edited
        // profile and MUST NOT be interpreted as an empty session.
        public bool TryRead(out ModelSessionSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (!File.Exists(_path))
            {
                if (File.Exists(_path + ".bak"))
                {
                    error = "Session primary is missing while a backup exists. " +
                            "Inspect the backup before any new checkpoint.";
                    return false;
                }
                return true;
            }
            return TryReadAt(_path, out snapshot, out error);
        }

        // Read-only recovery preview. This never modifies the primary or the
        // legacy PlayerPrefs key; manual restore requires a separate action.
        public bool TryReadBackup(out ModelSessionSnapshot snapshot, out string error)
        {
            return TryReadAt(_path + ".bak", out snapshot, out error);
        }

        public bool TrySaveExplicitCheckpoint(
            string rememberedPath, string activePath, out string error)
        {
            error = null;
            // Abort before writing if the old on-disk document is damaged,
            // unsupported or missing while its backup still exists.
            if (!TryRead(out _, out error))
                return false;

            if (!TryCanonicalVrmPath(rememberedPath, out var remembered, out error) ||
                !TryCanonicalVrmPath(activePath, out var active, out error))
                return false;

            var next = new ModelSessionSnapshot
            {
                Version = CurrentVersion,
                RememberedFormat = remembered.Length == 0 ? "" : "vrm",
                RememberedPath = remembered,
                ActiveFormat = active.Length == 0 ? "" : "vrm",
                ActivePath = active,
                CaptureReason = "operator",
                CapturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            try
            {
                var json = JsonUtility.ToJson(next, true);
                return BoundedTextFile.TryWriteUtf8Atomic(
                    _path, json, MaxBytes, out error, preserveBackup: true);
            }
            catch (Exception exception)
            {
                error = "Session checkpoint could not be saved: " +
                        exception.GetType().Name;
                return false;
            }
        }

        private static bool TryReadAt(
            string file, out ModelSessionSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (!File.Exists(file))
            {
                error = "Session snapshot file does not exist.";
                return false;
            }
            if (!BoundedTextFile.TryReadUtf8(file, MaxBytes,
                    out var json, out error))
                return false;
            try
            {
                var document = JsonUtility.FromJson<ModelSessionSnapshot>(json);
                if (document == null || document.Version != CurrentVersion)
                {
                    error = document != null && document.Version > CurrentVersion
                        ? "Session snapshot is from a newer unsupported version."
                        : "Session snapshot version is missing or unsupported.";
                    return false;
                }
                if (!ValidReference(document.RememberedPath,
                        document.RememberedFormat) ||
                    !ValidReference(document.ActivePath,
                        document.ActiveFormat) ||
                    document.CaptureReason != "operator" ||
                    !DateTime.TryParse(document.CapturedUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out _))
                {
                    error = "Session snapshot contains invalid references or metadata.";
                    return false;
                }
                snapshot = document;
                return true;
            }
            catch (Exception exception)
            {
                error = "Session snapshot is unreadable: " +
                        exception.GetType().Name;
                return false;
            }
        }

        private static bool ValidReference(string path, string format)
        {
            if (string.IsNullOrEmpty(path))
                return path == "" && format == "";
            return format == "vrm" &&
                   TryCanonicalVrmPath(path, out var normalized, out _) &&
                   string.Equals(path, normalized, StringComparison.Ordinal);
        }

        private static bool TryCanonicalVrmPath(
            string path, out string canonical, out string error)
        {
            canonical = "";
            error = null;
            if (string.IsNullOrEmpty(path))
                return true;
            if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength)
            {
                error = "Model snapshot path is missing or too long.";
                return false;
            }
            foreach (var ch in path)
            {
                if (char.IsControl(ch))
                {
                    error = "Model snapshot path contains a control character.";
                    return false;
                }
            }
            try
            {
                if (!Path.IsPathRooted(path) ||
                    !string.Equals(Path.GetExtension(path), ".vrm",
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Model snapshot accepts absolute VRM paths only.";
                    return false;
                }
                canonical = Path.GetFullPath(path);
                if (canonical.Length > MaxPathLength)
                {
                    error = "Model snapshot path is too long.";
                    return false;
                }
                // Never open or enumerate the referenced model file here.
                // A remembered file may be missing and must remain reviewable.
                return true;
            }
            catch (Exception exception)
            {
                error = "Model snapshot path is invalid: " + exception.GetType().Name;
                return false;
            }
        }
    }
}
