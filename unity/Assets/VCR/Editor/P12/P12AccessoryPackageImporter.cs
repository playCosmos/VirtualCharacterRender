using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VCR.Editor.P12
{
    internal sealed class P12AccessoryPackageImportResult
    {
        public P12AccessoryPackageManifest Manifest;
        public AppearanceAccessoryAnchorMode AnchorMode;
        public HumanBodyBones HumanoidBone;
        public string DestinationFolder;
        public string ManifestAssetPath;
        public string ModelAssetPath;
        public GameObject ModelAsset;
    }

    internal static class P12AccessoryPackageImporter
    {
        public const string DefaultDestinationRoot =
            "Assets/VCR/ImportedAccessories";

        private const long MaxModelBytes =
            256L * 1024L * 1024L;

        public static bool TryLoadManifest(
            string manifestFilePath,
            out P12AccessoryPackageManifest manifest,
            out AppearanceAccessoryAnchorMode anchorMode,
            out HumanBodyBones bone,
            out string error)
        {
            manifest = null;
            anchorMode =
                AppearanceAccessoryAnchorMode.None;
            bone =
                HumanBodyBones.Head;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    manifestFilePath) ||
                !File.Exists(
                    manifestFilePath))
            {
                error =
                    "Accessory package manifest file does not exist.";
                return false;
            }

            string json;

            try
            {
                json =
                    File.ReadAllText(
                        manifestFilePath);
            }
            catch (Exception exception)
            {
                error =
                    "Accessory package manifest read failed: " +
                    exception.Message;
                return false;
            }

            try
            {
                manifest =
                    JsonUtility.FromJson<
                        P12AccessoryPackageManifest>(
                        json);
            }
            catch (Exception exception)
            {
                error =
                    "Accessory package manifest JSON parse failed: " +
                    exception.Message;
                return false;
            }

            if (!P12AccessoryPackageManifestValidator
                .TryValidate(
                    manifest,
                    out anchorMode,
                    out bone,
                    out error))
            {
                return false;
            }

            if (!TryResolveModelPath(
                    manifestFilePath,
                    manifest.ModelFile,
                    out var modelPath,
                    out error))
            {
                return false;
            }

            if (!File.Exists(
                    modelPath))
            {
                error =
                    $"Accessory package model '{manifest.ModelFile}' does not exist.";
                return false;
            }

            try
            {
                var length =
                    new FileInfo(
                        modelPath)
                        .Length;

                if (length <= 0 ||
                    length >
                        MaxModelBytes)
                {
                    error =
                        $"Accessory package model size {length} bytes is outside the supported 1..{MaxModelBytes} byte range.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error =
                    "Accessory package model metadata read failed: " +
                    exception.Message;
                return false;
            }

            return true;
        }

        public static bool TryImport(
            string manifestFilePath,
            string destinationRoot,
            out P12AccessoryPackageImportResult result,
            out string error)
        {
            result = null;
            error = null;

            if (!TryLoadManifest(
                    manifestFilePath,
                    out var manifest,
                    out var anchorMode,
                    out var bone,
                    out error))
            {
                return false;
            }

            destinationRoot =
                NormalizeAssetFolder(
                    destinationRoot);

            var createdFolders =
                new List<string>();

            if (!TryEnsureAssetFolder(
                    destinationRoot,
                    createdFolders,
                    out error))
            {
                RollbackFolders(
                    createdFolders);
                return false;
            }

            var packageFolder =
                BuildUniquePackageFolder(
                    destinationRoot,
                    manifest.PackageId,
                    manifest.PackageVersion);

            if (!TryEnsureAssetFolder(
                    packageFolder,
                    createdFolders,
                    out error))
            {
                RollbackFolders(
                    createdFolders);
                return false;
            }

            var manifestAssetPath =
                packageFolder +
                "/manifest.vcraccessory.json";
            var modelAssetPath =
                packageFolder +
                "/" +
                SafeFileName(
                    Path.GetFileName(
                        manifest.ModelFile));

            try
            {
                if (!TryResolveModelPath(
                        manifestFilePath,
                        manifest.ModelFile,
                        out var sourceModelPath,
                        out error))
                {
                    RollbackFolders(
                        createdFolders);
                    return false;
                }

                File.Copy(
                    manifestFilePath,
                    AssetPathToAbsolutePath(
                        manifestAssetPath),
                    overwrite:
                        false);
                File.Copy(
                    sourceModelPath,
                    AssetPathToAbsolutePath(
                        modelAssetPath),
                    overwrite:
                        false);

                AssetDatabase.ImportAsset(
                    manifestAssetPath,
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);
                AssetDatabase.ImportAsset(
                    modelAssetPath,
                    ImportAssetOptions
                        .ForceSynchronousImport |
                    ImportAssetOptions
                        .ForceUpdate);

                var model =
                    AssetDatabase.LoadAssetAtPath<
                        GameObject>(
                        modelAssetPath);

                if (model == null)
                {
                    error =
                        "Accessory package FBX did not import as a GameObject.";
                    RollbackFolders(
                        createdFolders);
                    return false;
                }

                if (model.GetComponentsInChildren<
                        SkinnedMeshRenderer>(
                        true)
                    .Length > 0)
                {
                    error =
                        "Accessory package v1 rejects skinned meshes. Use the future compatible-skinned-outfit/accessory path after skeleton compatibility validation.";
                    RollbackFolders(
                        createdFolders);
                    return false;
                }

                if (model.GetComponentsInChildren<
                        MeshRenderer>(
                        true)
                    .Length == 0)
                {
                    error =
                        "Accessory package FBX contains no rigid MeshRenderer.";
                    RollbackFolders(
                        createdFolders);
                    return false;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                result =
                    new P12AccessoryPackageImportResult
                    {
                        Manifest =
                            manifest,
                        AnchorMode =
                            anchorMode,
                        HumanoidBone =
                            bone,
                        DestinationFolder =
                            packageFolder,
                        ManifestAssetPath =
                            manifestAssetPath,
                        ModelAssetPath =
                            modelAssetPath,
                        ModelAsset =
                            model
                    };

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Accessory package import failed: " +
                    exception.Message;
                RollbackFolders(
                    createdFolders);
                return false;
            }
        }

        internal static bool TryResolveModelPath(
            string manifestFilePath,
            string relativeModelPath,
            out string modelPath,
            out string error)
        {
            modelPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    manifestFilePath) ||
                string.IsNullOrWhiteSpace(
                    relativeModelPath))
            {
                error =
                    "Accessory package manifest/model path is missing.";
                return false;
            }

            var root =
                Path.GetFullPath(
                    Path.GetDirectoryName(
                        manifestFilePath) ??
                    string.Empty);
            var combined =
                Path.GetFullPath(
                    Path.Combine(
                        root,
                        relativeModelPath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
            var prefix =
                root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!combined.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Accessory package model path escapes the package directory.";
                return false;
            }

            modelPath =
                combined;
            return true;
        }

        private static string BuildUniquePackageFolder(
            string destinationRoot,
            string packageId,
            string packageVersion)
        {
            var baseName =
                SafeFileName(
                    packageId +
                    "-" +
                    packageVersion);
            var candidate =
                destinationRoot +
                "/" +
                baseName;
            var suffix = 2;

            while (AssetDatabase.IsValidFolder(
                       candidate) ||
                   Directory.Exists(
                       AssetPathToAbsolutePath(
                           candidate)))
            {
                candidate =
                    destinationRoot +
                    "/" +
                    baseName +
                    "-" +
                    suffix++;
            }

            return candidate;
        }

        private static bool TryEnsureAssetFolder(
            string assetFolder,
            ICollection<string> createdFolders,
            out string error)
        {
            error = null;

            if (!string.Equals(
                    assetFolder,
                    "Assets",
                    StringComparison.Ordinal) &&
                !assetFolder.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                error =
                    "Accessory package destination must be inside Assets.";
                return false;
            }

            if (AssetDatabase.IsValidFolder(
                    assetFolder))
            {
                return true;
            }

            var parts =
                assetFolder.Split(
                    new[]
                    {
                        '/'
                    },
                    StringSplitOptions
                        .RemoveEmptyEntries);
            var current =
                "Assets";

            for (var i = 1;
                 i < parts.Length;
                 i++)
            {
                var next =
                    current +
                    "/" +
                    parts[i];

                if (!AssetDatabase.IsValidFolder(
                        next))
                {
                    var guid =
                        AssetDatabase.CreateFolder(
                            current,
                            parts[i]);

                    if (string.IsNullOrWhiteSpace(
                            guid))
                    {
                        error =
                            $"Could not create accessory import folder '{next}'.";
                        return false;
                    }

                    createdFolders?.Add(
                        next);
                }

                current =
                    next;
            }

            return true;
        }

        private static void RollbackFolders(
            IEnumerable<string> createdFolders)
        {
            if (createdFolders == null)
            {
                return;
            }

            var list =
                new List<string>(
                    createdFolders);

            for (var i = list.Count - 1;
                 i >= 0;
                 i--)
            {
                var folder =
                    list[i];

                if (!string.IsNullOrWhiteSpace(
                        folder) &&
                    AssetDatabase.IsValidFolder(
                        folder))
                {
                    AssetDatabase.DeleteAsset(
                        folder);
                }
            }

            AssetDatabase.Refresh();
        }

        private static string NormalizeAssetFolder(
            string path)
        {
            path =
                string.IsNullOrWhiteSpace(
                    path)
                    ? DefaultDestinationRoot
                    : path.Trim();

            return path
                .Replace(
                    '\\',
                    '/')
                .TrimEnd('/');
        }

        private static string AssetPathToAbsolutePath(
            string assetPath)
        {
            var root =
                Directory.GetParent(
                        Application.dataPath)
                    ?.FullName;

            if (string.IsNullOrWhiteSpace(
                    root))
            {
                throw new InvalidOperationException(
                    "Unity project root could not be resolved.");
            }

            return Path.Combine(
                root,
                assetPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        private static string SafeFileName(
            string value)
        {
            value =
                string.IsNullOrWhiteSpace(
                    value)
                    ? "accessory"
                    : value.Trim();

            foreach (var invalid in
                     Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        invalid,
                        '_');
            }

            return value;
        }
    }
}
