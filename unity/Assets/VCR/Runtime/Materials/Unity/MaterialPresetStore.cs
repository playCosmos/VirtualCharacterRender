using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Materials.Unity
{
    public sealed class MaterialPresetStore
    {
        public const int CurrentVersion = 1;

        private const long MaxFileBytes =
            16L * 1024L * 1024L;

        private readonly string _path;

        public MaterialPresetStore(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Preset path is required.",
                    nameof(path));
            }

            _path =
                System.IO.Path.GetFullPath(
                    path);
        }

        public string Path =>
            _path;

        public bool TryLoad(
            out MaterialPresetDocument document,
            out string error)
        {
            document =
                new MaterialPresetDocument
                {
                    Version =
                        CurrentVersion
                };
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
                        "Material preset load failed: " +
                        readError;
                    return false;
                }

                var loaded =
                    JsonUtility.FromJson<
                        MaterialPresetDocument>(
                        json);

                if (!TryValidate(
                        loaded,
                        out error))
                {
                    return false;
                }

                document = loaded;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Material preset load failed: " +
                    exception.Message;
                return false;
            }
        }

        public bool TrySave(
            MaterialPresetDocument document,
            out string error)
        {
            error = null;

            if (!TryValidate(
                    document,
                    out error))
            {
                return false;
            }

            try
            {
                var json =
                    JsonUtility.ToJson(
                        document,
                        prettyPrint: true);

                if (!BoundedTextFile.TryWriteUtf8Atomic(
                        _path,
                        json,
                        MaxFileBytes,
                        out var writeError))
                {
                    error =
                        "Material preset save failed: " +
                        writeError;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Material preset save failed: " +
                    exception.Message;
                return false;
            }
        }

        private bool TryValidate(
            MaterialPresetDocument document,
            out string error)
        {
            error = null;

            if (document == null)
            {
                error =
                    "Material preset document is required.";
                return false;
            }

            if (document.Version !=
                CurrentVersion)
            {
                error =
                    document.Version >
                    CurrentVersion
                        ? $"Material preset version {document.Version} is newer than supported version {CurrentVersion}."
                        : $"Material preset version {document.Version} is unsupported and has no migration path.";
                return false;
            }

            document.Presets ??=
                Array.Empty<
                    MaterialOverridePreset>();

            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var preset in
                     document.Presets)
            {
                if (preset == null)
                {
                    error =
                        "Material preset document contains a null preset.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(
                        preset.PresetId))
                {
                    error =
                        "Every material preset requires a preset id.";
                    return false;
                }

                if (!ids.Add(
                        preset.PresetId))
                {
                    error =
                        $"Duplicate material preset id '{preset.PresetId}'.";
                    return false;
                }

                preset.Parameters ??=
                    Array.Empty<
                        MaterialParameterOverride>();
            }

            return true;
        }

    }
}
