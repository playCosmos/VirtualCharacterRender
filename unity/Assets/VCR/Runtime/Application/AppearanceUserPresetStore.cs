using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using VCR.Runtime.Appearance;

namespace VCR.Runtime.Application
{
    [Serializable]
    public sealed class AppearanceUserPresetProfile
    {
        public int Version;
        public string CharacterPath;
        public AppearancePreset[] Presets =
            Array.Empty<AppearancePreset>();
    }

    public sealed class AppearanceUserPresetStore
    {
        public const int CurrentVersion = 1;

        private readonly string _directory;

        public AppearanceUserPresetStore(
            string directory)
        {
            if (string.IsNullOrWhiteSpace(
                    directory))
            {
                throw new ArgumentException(
                    "Appearance profile directory is required.",
                    nameof(directory));
            }

            _directory =
                Path.GetFullPath(
                    directory);
        }

        public string DirectoryPath =>
            _directory;

        public static AppearanceUserPresetStore
            CreateDefault()
        {
            return new AppearanceUserPresetStore(
                Path.Combine(
                    Application.persistentDataPath,
                    "appearance-profiles"));
        }

        public string GetProfilePath(
            string characterPath)
        {
            var key =
                GetProfileKey(
                    characterPath);

            return Path.Combine(
                _directory,
                key + ".json");
        }

        public bool TryLoad(
            string characterPath,
            out AppearancePreset[] presets,
            out string error)
        {
            presets =
                Array.Empty<AppearancePreset>();
            error = null;

            if (!TryNormalizeCharacterPath(
                    characterPath,
                    out var normalized,
                    out error))
            {
                return false;
            }

            var path =
                GetProfilePath(
                    normalized);

            if (!File.Exists(
                    path))
            {
                return true;
            }

            try
            {
                var json =
                    File.ReadAllText(
                        path);

                var profile =
                    JsonUtility.FromJson<
                        AppearanceUserPresetProfile>(
                        json);

                if (profile == null)
                {
                    error =
                        "Appearance profile JSON did not produce a valid document.";
                    return false;
                }

                if (profile.Version !=
                    CurrentVersion)
                {
                    error =
                        profile.Version >
                        CurrentVersion
                            ? $"Appearance profile version {profile.Version} is newer than supported version {CurrentVersion}."
                            : $"Appearance profile version {profile.Version} is unsupported and has no migration path.";
                    return false;
                }

                presets =
                    ClonePresets(
                        profile.Presets);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Appearance profile load failed: " +
                    exception.Message;
                return false;
            }
        }

        public bool TrySave(
            string characterPath,
            AppearancePreset[] presets,
            out string error)
        {
            error = null;

            if (!TryNormalizeCharacterPath(
                    characterPath,
                    out var normalized,
                    out error))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(
                    _directory);

                var profile =
                    new AppearanceUserPresetProfile
                    {
                        Version =
                            CurrentVersion,
                        CharacterPath =
                            normalized,
                        Presets =
                            ClonePresets(
                                presets)
                    };

                var json =
                    JsonUtility.ToJson(
                        profile,
                        prettyPrint: true);

                var path =
                    GetProfilePath(
                        normalized);
                var temporaryPath =
                    path + ".tmp";
                var backupPath =
                    path + ".bak";

                File.WriteAllText(
                    temporaryPath,
                    json);

                if (File.Exists(
                        path))
                {
                    File.Replace(
                        temporaryPath,
                        path,
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
                        path);
                }

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    var path =
                        GetProfilePath(
                            normalized);
                    var temporaryPath =
                        path + ".tmp";

                    if (File.Exists(
                            temporaryPath))
                    {
                        File.Delete(
                            temporaryPath);
                    }
                }
                catch
                {
                    // Preserve the original persistence failure.
                }

                error =
                    "Appearance profile save failed: " +
                    exception.Message;
                return false;
            }
        }

        public static string GetProfileKey(
            string characterPath)
        {
            if (!TryNormalizeCharacterPath(
                    characterPath,
                    out var normalized,
                    out var error))
            {
                throw new ArgumentException(
                    error,
                    nameof(characterPath));
            }

            using var sha256 =
                SHA256.Create();

            var bytes =
                Encoding.UTF8.GetBytes(
                    normalized);
            var hash =
                sha256.ComputeHash(
                    bytes);
            var builder =
                new StringBuilder(
                    hash.Length * 2);

            foreach (var value in hash)
            {
                builder.Append(
                    value.ToString("x2"));
            }

            return builder.ToString();
        }

        public static bool TryNormalizeCharacterPath(
            string characterPath,
            out string normalized,
            out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    characterPath))
            {
                error =
                    "Character path is required for appearance profile persistence.";
                return false;
            }

            try
            {
                normalized =
                    Path.GetFullPath(
                            characterPath.Trim())
                        .Replace(
                            '\\',
                            '/');
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Character path normalization failed: " +
                    exception.Message;
                return false;
            }
        }

        private static AppearancePreset[]
            ClonePresets(
                AppearancePreset[] source)
        {
            source ??=
                Array.Empty<AppearancePreset>();

            var result =
                new AppearancePreset[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var preset =
                    source[i];

                if (preset == null)
                {
                    result[i] = null;
                    continue;
                }

                var accessories =
                    preset.Accessories ??
                    Array.Empty<
                        AppearanceAccessorySelection>();
                var accessoryClones =
                    new AppearanceAccessorySelection[
                        accessories.Length];

                for (var accessoryIndex = 0;
                     accessoryIndex < accessories.Length;
                     accessoryIndex++)
                {
                    accessoryClones[
                        accessoryIndex] =
                            accessories[
                                accessoryIndex]?.Clone();
                }

                result[i] =
                    new AppearancePreset
                    {
                        Id = preset.Id,
                        OutfitId =
                            preset.OutfitId,
                        PreferredTransitionId =
                            preset.PreferredTransitionId,
                        Accessories =
                            accessoryClones
                    };
            }

            return result;
        }
    }
}
