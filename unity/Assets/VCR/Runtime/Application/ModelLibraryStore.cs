using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Application
{
    [Serializable]
    public sealed class ModelLibraryEntry
    {
        public string Path;
        public string Format;
    }

    [Serializable]
    internal sealed class ModelLibraryDocument
    {
        public int Version;
        public ModelLibraryEntry[] Entries;
    }

    // Explicitly registered local VRM references only. Not an importer,
    // filesystem indexer, or owner of the active model / last-successful key.
    public sealed class ModelLibraryStore
    {
        public const int CurrentVersion = 1;
        public const int MaxEntries = 128;
        public const int MaxPathLength = 4096;
        private const long MaxDocumentBytes = 512L * 1024L;

        private readonly string _filePath;
        private static readonly StringComparer PathComparer =
            System.IO.Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        public ModelLibraryStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Model library path is required.", nameof(filePath));
            _filePath = System.IO.Path.GetFullPath(filePath);
        }

        public string FilePath => _filePath;

        public static ModelLibraryStore CreateDefault()
        {
            return new ModelLibraryStore(System.IO.Path.Combine(
                UnityEngine.Application.persistentDataPath, "model-library.v1.json"));
        }

        public bool TryRead(out ModelLibraryEntry[] entries, out string error)
        {
            entries = Array.Empty<ModelLibraryEntry>();
            error = null;
            if (!File.Exists(_filePath))
            {
                if (File.Exists(_filePath + ".bak"))
                {
                    error = "Model library primary is missing but a backup exists. " +
                            "Restore/review the backup manually; automatic creation is blocked.";
                    return false;
                }
                return true;
            }

            if (!BoundedTextFile.TryReadUtf8(_filePath, MaxDocumentBytes,
                    out var json, out error))
                return false;
            try
            {
                var document = JsonUtility.FromJson<ModelLibraryDocument>(json);
                if (document == null || document.Version != CurrentVersion)
                {
                    error = document != null && document.Version > CurrentVersion
                        ? "Model library version is newer than supported. Existing data is protected."
                        : "Model library document version is missing or unsupported.";
                    return false;
                }
                if (!TryValidateEntries(document.Entries, out error))
                    return false;
                entries = Clone(document.Entries);
                return true;
            }
            catch (Exception exception)
            {
                error = "Model library JSON is unreadable: " + exception.GetType().Name;
                return false;
            }
        }

        public bool TryAdd(string modelPath, out string error)
        {
            if (!TryCanonicalPath(modelPath, true, out var normalized, out error))
                return false;
            if (!TryRead(out var old, out error))
                return false;
            foreach (var entry in old)
            {
                if (PathComparer.Equals(entry.Path, normalized))
                {
                    error = "This VRM is already registered.";
                    return false;
                }
            }
            if (old.Length >= MaxEntries)
            {
                error = "Model library is full (" + MaxEntries + " entries).";
                return false;
            }

            var next = new ModelLibraryEntry[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = new ModelLibraryEntry
                { Path = normalized, Format = "vrm" };
            return TryWrite(next, out error);
        }

        public bool TryRemove(string modelPath, out string error)
        {
            if (!TryCanonicalPath(modelPath, false, out var normalized, out error))
                return false;
            if (!TryRead(out var old, out error))
                return false;
            var index = Array.FindIndex(old, e => PathComparer.Equals(e.Path, normalized));
            if (index < 0)
            {
                error = "This VRM is not registered.";
                return false;
            }
            var next = new ModelLibraryEntry[old.Length - 1];
            if (index > 0)
                Array.Copy(old, 0, next, 0, index);
            if (index < next.Length)
                Array.Copy(old, index + 1, next, index, next.Length - index);
            // Removing a catalog reference never deletes model bytes or the
            // separate last-successful model preference.
            return TryWrite(next, out error);
        }

        private bool TryWrite(ModelLibraryEntry[] entries, out string error)
        {
            if (!TryValidateEntries(entries, out error))
                return false;
            try
            {
                var document = new ModelLibraryDocument
                    { Version = CurrentVersion, Entries = Clone(entries) };
                var json = JsonUtility.ToJson(document, true);
                return BoundedTextFile.TryWriteUtf8Atomic(
                    _filePath, json, MaxDocumentBytes, out error,
                    preserveBackup: true);
            }
            catch (Exception exception)
            {
                error = "Model library save failed: " + exception.GetType().Name;
                return false;
            }
        }

        private static bool TryValidateEntries(ModelLibraryEntry[] entries, out string error)
        {
            error = null;
            if (entries == null || entries.Length > MaxEntries)
            {
                error = "Model library entries are missing or exceed the supported limit.";
                return false;
            }
            var seen = new HashSet<string>(PathComparer);
            foreach (var entry in entries)
            {
                if (entry == null ||
                    !string.Equals(entry.Format, "vrm", StringComparison.Ordinal) ||
                    !TryCanonicalPath(entry.Path, false, out var canonical, out error) ||
                    !string.Equals(canonical, entry.Path, StringComparison.Ordinal) ||
                    !seen.Add(canonical))
                {
                    error = "Model library contains an invalid, noncanonical or duplicate VRM path.";
                    return false;
                }
            }
            return true;
        }

        private static bool TryCanonicalPath(
            string rawPath, bool requireFile, out string normalized, out string error)
        {
            normalized = null;
            error = null;
            if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Length > MaxPathLength)
            {
                error = "A bounded absolute VRM path is required.";
                return false;
            }
            foreach (var character in rawPath)
            {
                if (char.IsControl(character))
                {
                    error = "Model path contains a control character.";
                    return false;
                }
            }
            try
            {
                var candidate = rawPath.Trim();
                if (!System.IO.Path.IsPathRooted(candidate) ||
                    !string.Equals(System.IO.Path.GetExtension(candidate), ".vrm",
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Only absolute .vrm file paths are supported.";
                    return false;
                }
                normalized = System.IO.Path.GetFullPath(candidate);
                if (normalized.Length > MaxPathLength ||
                    (requireFile && !File.Exists(normalized)))
                {
                    error = requireFile
                        ? "The selected VRM file does not exist or its path is too long."
                        : "The model path exceeds the supported limit.";
                    normalized = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "Invalid VRM path: " + exception.GetType().Name;
                return false;
            }
        }

        private static ModelLibraryEntry[] Clone(ModelLibraryEntry[] entries)
        {
            var clone = new ModelLibraryEntry[entries.Length];
            for (var index = 0; index < entries.Length; index++)
                clone[index] = new ModelLibraryEntry
                    { Path = entries[index].Path, Format = entries[index].Format };
            return clone;
        }
    }
}
