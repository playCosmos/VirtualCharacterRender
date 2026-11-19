using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Application
{
    // A bounded, explicit proposal. It is never applied during startup.
    public sealed class ModelSessionAdoptionPreview
    {
        public string TargetPath;
        public bool PreviousPresent;
        public string PreviousPath;
        public string SnapshotFingerprint;
    }

    [Serializable]
    internal sealed class ModelSessionAdoptionJournal
    {
        public int Version;
        public bool PreviousPresent;
        public string PreviousPath;
        public bool TargetPresent;
        public string TargetPath;
        public string SnapshotFingerprint;
        public string State;
    }

    // Opt-in bridge from a reviewed snapshot to the existing authoritative
    // PlayerPrefs v1 preference. The live model is never loaded or unloaded.
    // A durable rollback journal is written BEFORE the preference changes.
    public sealed class ModelSessionAdoptionService
    {
        public const int CurrentVersion = 1;
        private const long MaxJournalBytes = 32L * 1024L;
        private const long MaxSnapshotBytes = 32L * 1024L;
        private const int MaxPreferenceLength = 4096;
        private readonly ModelSessionSnapshotStore _snapshotStore;
        private readonly string _journalPath;

        public ModelSessionAdoptionService(
            ModelSessionSnapshotStore snapshotStore, string journalPath)
        {
            _snapshotStore = snapshotStore ??
                throw new ArgumentNullException(nameof(snapshotStore));
            if (string.IsNullOrWhiteSpace(journalPath))
                throw new ArgumentException("Rollback journal path is required.",
                    nameof(journalPath));
            _journalPath = Path.GetFullPath(journalPath);
            if (string.Equals(_journalPath, _snapshotStore.FilePath,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Journal and snapshot paths must differ.");
        }

        public static ModelSessionAdoptionService CreateDefault()
        {
            return new ModelSessionAdoptionService(
                ModelSessionSnapshotStore.CreateDefault(),
                Path.Combine(UnityEngine.Application.persistentDataPath,
                    "model-session-adoption-rollback.v1.json"));
        }

        public bool TryPreview(out ModelSessionAdoptionPreview preview,
            out string error)
        {
            preview = null;
            if (!_snapshotStore.TryRead(out var snapshot, out error))
                return false;
            if (snapshot == null)
            {
                error = "Create or restore a reviewed session checkpoint first.";
                return false;
            }

            var target = snapshot.RememberedPath;
            if (!string.IsNullOrEmpty(target) && !File.Exists(target))
            {
                error = "The checkpoint's remembered VRM file is missing; " +
                        "adoption is disabled until the file is available.";
                return false;
            }

            // Hash the same bounded serialized file for a stale-preview guard.
            // A second read protects against races between parse and hashing;
            // TryAdopt re-reads and re-validates before changing PlayerPrefs.
            if (!BoundedTextFile.TryReadUtf8(_snapshotStore.FilePath,
                    MaxSnapshotBytes, out var text, out error))
                return false;
            var previous = ModelSessionController.RememberedPath;
            if (previous.Length > MaxPreferenceLength)
            {
                error = "Existing preference exceeds rollback journal limits.";
                return false;
            }
            preview = new ModelSessionAdoptionPreview
            {
                TargetPath = target,
                PreviousPresent = PlayerPrefs.HasKey(ModelSessionController.PreferenceKey),
                PreviousPath = previous,
                SnapshotFingerprint = Fingerprint(text)
            };
            return true;
        }

        public bool TryAdopt(ModelSessionAdoptionPreview reviewed,
            out string error)
        {
            error = null;
            if (reviewed == null)
            {
                error = "Review the checkpoint before adoption.";
                return false;
            }
            if (!TryPreview(out var current, out error))
                return false;
            if (current.SnapshotFingerprint != reviewed.SnapshotFingerprint ||
                current.TargetPath != reviewed.TargetPath ||
                current.PreviousPresent != reviewed.PreviousPresent ||
                current.PreviousPath != reviewed.PreviousPath)
            {
                error = "Checkpoint or legacy preference changed after review.";
                return false;
            }

            var targetPresent = !string.IsNullOrEmpty(current.TargetPath);
            if (targetPresent == current.PreviousPresent &&
                (!targetPresent || current.TargetPath == current.PreviousPath))
            {
                error = "Checkpoint matches the existing remembered preference.";
                return false;
            }
            if (!TryReadJournal(out var existing, out error))
                return false;
            if (existing != null && existing.State == "pending")
            {
                error = "Resolve the previous adoption (rollback or keep) first.";
                return false;
            }

            var journal = new ModelSessionAdoptionJournal
            {
                Version = CurrentVersion,
                PreviousPresent = current.PreviousPresent,
                PreviousPath = current.PreviousPath,
                TargetPresent = targetPresent,
                TargetPath = current.TargetPath,
                SnapshotFingerprint = current.SnapshotFingerprint,
                State = "pending"
            };
            if (!TryWriteJournal(journal, out error))
                return false;
            try
            {
                ApplyPreference(targetPresent, current.TargetPath);
                return true;
            }
            catch (Exception exception)
            {
                // A pending journal remains for recovery even if Save failed.
                try
                {
                    ApplyPreference(current.PreviousPresent, current.PreviousPath);
                }
                catch { /* Preserve journal for operator recovery. */ }
                error = "Adoption preference save failed: " +
                        exception.GetType().Name;
                return false;
            }
        }

        public bool TryRollback(out string error)
        {
            if (!TryReadPending(out var journal, out error))
                return false;
            if (!MatchesPreference(journal.TargetPresent, journal.TargetPath) &&
                !MatchesPreference(journal.PreviousPresent, journal.PreviousPath))
            {
                error = "The remembered preference changed after adoption; " +
                        "rollback must not overwrite an operator's newer choice.";
                return false;
            }

            // If the application stopped after journaling but before applying,
            // the original preference is already intact. Closing the pending
            // journal is an idempotent, recoverable rollback.
            if (!MatchesPreference(journal.PreviousPresent, journal.PreviousPath))
            {
                try
                {
                    ApplyPreference(journal.PreviousPresent, journal.PreviousPath);
                }
                catch (Exception exception)
                {
                    error = "Rollback preference save failed: " +
                            exception.GetType().Name;
                    return false;
                }
            }
            journal.State = "closed";
            // A failed close leaves the pending journal; a subsequent
            // rollback can safely finish because the previous value matches.
            return TryWriteJournal(journal, out error);
        }

        public bool TryKeepAdoptedPreference(out string error)
        {
            if (!TryReadPending(out var journal, out error))
                return false;
            if (!MatchesPreference(journal.TargetPresent, journal.TargetPath))
            {
                error = "The remembered preference changed after adoption; " +
                        "cannot close the rollback journal.";
                return false;
            }
            journal.State = "closed";
            return TryWriteJournal(journal, out error);
        }

        private bool TryReadPending(out ModelSessionAdoptionJournal journal,
            out string error)
        {
            if (!TryReadJournal(out journal, out error))
                return false;
            if (journal == null || journal.State != "pending")
            {
                error = "No pending checkpoint adoption exists.";
                return false;
            }
            return true;
        }

        private bool TryReadJournal(out ModelSessionAdoptionJournal journal,
            out string error)
        {
            journal = null;
            error = null;
            if (!File.Exists(_journalPath))
            {
                if (File.Exists(_journalPath + ".bak"))
                {
                    error = "Rollback journal primary is missing while its backup exists.";
                    return false;
                }
                return true;
            }
            if (!BoundedTextFile.TryReadUtf8(_journalPath, MaxJournalBytes,
                    out var json, out error))
                return false;
            try
            {
                var data = JsonUtility.FromJson<ModelSessionAdoptionJournal>(json);
                if (!ValidJournal(data))
                {
                    error = "Rollback journal is damaged or its version is unsupported.";
                    return false;
                }
                journal = data;
                return true;
            }
            catch (Exception exception)
            {
                error = "Rollback journal is unreadable: " +
                        exception.GetType().Name;
                return false;
            }
        }

        private bool TryWriteJournal(ModelSessionAdoptionJournal journal,
            out string error)
        {
            if (!ValidJournal(journal))
            {
                error = "Rollback journal would be invalid.";
                return false;
            }
            try
            {
                return BoundedTextFile.TryWriteUtf8Atomic(_journalPath,
                    JsonUtility.ToJson(journal, true), MaxJournalBytes,
                    out error, preserveBackup: true);
            }
            catch (Exception exception)
            {
                error = "Rollback journal write failed: " +
                        exception.GetType().Name;
                return false;
            }
        }

        private static bool ValidJournal(ModelSessionAdoptionJournal journal)
        {
            if (journal == null || journal.Version != CurrentVersion ||
                (journal.State != "pending" && journal.State != "closed") ||
                journal.PreviousPath == null || journal.TargetPath == null ||
                journal.PreviousPath.Length > MaxPreferenceLength ||
                journal.TargetPath.Length > MaxPreferenceLength ||
                (!journal.PreviousPresent && journal.PreviousPath.Length != 0) ||
                journal.TargetPresent != (journal.TargetPath.Length > 0) ||
                journal.SnapshotFingerprint == null ||
                journal.SnapshotFingerprint.Length != 64)
                return false;
            foreach (var ch in journal.SnapshotFingerprint)
                if (!Uri.IsHexDigit(ch))
                    return false;
            if (!journal.TargetPresent)
                return true;
            try
            {
                return Path.IsPathRooted(journal.TargetPath) &&
                       string.Equals(Path.GetExtension(journal.TargetPath),
                           ".vrm", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(Path.GetFullPath(journal.TargetPath),
                           journal.TargetPath, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static bool MatchesPreference(bool present, string value)
        {
            return PlayerPrefs.HasKey(ModelSessionController.PreferenceKey) == present &&
                   (!present || ModelSessionController.RememberedPath == value);
        }

        private static void ApplyPreference(bool present, string value)
        {
            if (present)
                PlayerPrefs.SetString(ModelSessionController.PreferenceKey, value);
            else
                PlayerPrefs.DeleteKey(ModelSessionController.PreferenceKey);
            PlayerPrefs.Save();
        }

        private static string Fingerprint(string text)
        {
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var result = new StringBuilder(digest.Length * 2);
            foreach (var b in digest)
                result.Append(b.ToString("x2"));
            return result.ToString();
        }
    }
}
