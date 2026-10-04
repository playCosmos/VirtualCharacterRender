using System;
using System.IO;
using UnityEngine;
using VCR.Runtime.Appearance.Unity;

namespace VCR.Editor.P12
{
    [Serializable]
    internal sealed class P12AccessoryPackageManifest
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion =
            CurrentFormatVersion;
        public string PackageId;
        public string PackageVersion;
        public string SlotId;
        public string AccessoryId;
        public string ModelFile;
        public string AnchorMode =
            "None";
        public string HumanoidBone =
            "Head";
        public Vector3 LocalPosition =
            Vector3.zero;
        public Vector3 LocalEulerAngles =
            Vector3.zero;
        public bool OverrideLocalScale = false;
        public Vector3 LocalScale =
            Vector3.one;
        public bool RestoreOriginalTransformWhenInactive =
            true;
    }

    internal static class P12AccessoryPackageManifestValidator
    {
        public static bool TryValidate(
            P12AccessoryPackageManifest manifest,
            out AppearanceAccessoryAnchorMode anchorMode,
            out HumanBodyBones bone,
            out string error)
        {
            anchorMode =
                AppearanceAccessoryAnchorMode.None;
            bone =
                HumanBodyBones.Head;
            error = null;

            if (manifest == null)
            {
                error =
                    "Accessory package manifest is required.";
                return false;
            }

            if (manifest.FormatVersion !=
                P12AccessoryPackageManifest
                    .CurrentFormatVersion)
            {
                error =
                    $"Accessory package format {manifest.FormatVersion} is unsupported; expected {P12AccessoryPackageManifest.CurrentFormatVersion}.";
                return false;
            }

            if (!IsSafeIdentifier(
                    manifest.PackageId) ||
                !IsSafeVersion(
                    manifest.PackageVersion) ||
                !IsSafeIdentifier(
                    manifest.SlotId) ||
                !IsSafeIdentifier(
                    manifest.AccessoryId))
            {
                error =
                    "Accessory package requires safe package/version/slot/accessory identifiers.";
                return false;
            }

            if (!IsSafeRelativePath(
                    manifest.ModelFile))
            {
                error =
                    "Accessory package ModelFile must be a safe canonical relative path.";
                return false;
            }

            if (!string.Equals(
                    Path.GetExtension(
                        manifest.ModelFile),
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Accessory package v1 accepts only .fbx model files.";
                return false;
            }

            if (string.Equals(
                    manifest.AnchorMode,
                    "None",
                    StringComparison.OrdinalIgnoreCase))
            {
                anchorMode =
                    AppearanceAccessoryAnchorMode.None;
            }
            else if (string.Equals(
                         manifest.AnchorMode,
                         "HumanoidBone",
                         StringComparison.OrdinalIgnoreCase))
            {
                anchorMode =
                    AppearanceAccessoryAnchorMode
                        .HumanoidBone;

                if (!Enum.TryParse(
                        manifest.HumanoidBone,
                        true,
                        out bone) ||
                    bone ==
                        HumanBodyBones.LastBone)
                {
                    error =
                        $"Accessory package humanoid bone '{manifest.HumanoidBone ?? "<null>"}' is invalid.";
                    return false;
                }
            }
            else
            {
                error =
                    $"Accessory package anchor mode '{manifest.AnchorMode ?? "<null>"}' is unsupported. Use None or HumanoidBone.";
                return false;
            }

            if (!IsFinite(
                    manifest.LocalPosition) ||
                !IsFinite(
                    manifest.LocalEulerAngles) ||
                !IsFinite(
                    manifest.LocalScale))
            {
                error =
                    "Accessory package anchor offsets must contain finite values.";
                return false;
            }

            if (manifest.OverrideLocalScale &&
                (Mathf.Abs(
                     manifest.LocalScale.x) <
                     0.000001f ||
                 Mathf.Abs(
                     manifest.LocalScale.y) <
                     0.000001f ||
                 Mathf.Abs(
                     manifest.LocalScale.z) <
                     0.000001f))
            {
                error =
                    "Accessory package local scale override cannot contain a zero axis.";
                return false;
            }

            return true;
        }

        public static bool IsSafeRelativePath(
            string path)
        {
            if (string.IsNullOrWhiteSpace(
                    path) ||
                path.Length > 512 ||
                path[0] == '/' ||
                path.Contains(
                    "\\",
                    StringComparison.Ordinal) ||
                path.Contains(
                    ":",
                    StringComparison.Ordinal) ||
                path.Contains(
                    "?",
                    StringComparison.Ordinal) ||
                path.Contains(
                    "#",
                    StringComparison.Ordinal) ||
                path.Contains(
                    "\0",
                    StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var segment in
                     path.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(
                        segment) ||
                    string.Equals(
                        segment,
                        ".",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        segment,
                        "..",
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSafeIdentifier(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length > 128 ||
                value == "." ||
                value == "..")
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(
                        ch) ||
                    ch == '.' ||
                    ch == '_' ||
                    ch == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsSafeVersion(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value) ||
                value.Length > 64)
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(
                        ch) ||
                    ch == '.' ||
                    ch == '+' ||
                    ch == '_' ||
                    ch == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsFinite(
            Vector3 value) =>
                !float.IsNaN(
                    value.x) &&
                !float.IsInfinity(
                    value.x) &&
                !float.IsNaN(
                    value.y) &&
                !float.IsInfinity(
                    value.y) &&
                !float.IsNaN(
                    value.z) &&
                !float.IsInfinity(
                    value.z);
    }
}
