using System;
using System.Collections.Generic;

namespace VCR.Runtime.Materials
{
    public static class ShaderPackageManifestValidator
    {
        private static readonly string[] BundleExtensions =
        {
            ".bundle"
        };

        private static readonly string[] JsonExtensions =
        {
            ".json"
        };

        private static readonly string[] ImageExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".tga"
        };

        private static readonly HashSet<string> ForbiddenExtensions =
            new(
                new[]
                {
                    ".dll",
                    ".exe",
                    ".dylib",
                    ".so",
                    ".jar",
                    ".js",
                    ".mjs",
                    ".cjs",
                    ".py",
                    ".ps1",
                    ".bat",
                    ".cmd",
                    ".sh",
                    ".shader",
                    ".hlsl",
                    ".cginc",
                    ".glsl",
                    ".metal"
                },
                StringComparer.OrdinalIgnoreCase);

        public static bool TryValidate(
            ShaderPackageManifest manifest,
            out string error)
        {
            error = null;

            if (manifest == null)
            {
                error =
                    "Shader package manifest is required.";
                return false;
            }

            if (manifest.FormatVersion !=
                ShaderPackageManifest.CurrentFormatVersion)
            {
                error =
                    $"Shader package format {manifest.FormatVersion} is unsupported; expected {ShaderPackageManifest.CurrentFormatVersion}.";
                return false;
            }

            if (!IsSafeIdentifier(
                    manifest.PackageId))
            {
                error =
                    "Shader package requires a safe package id using letters, digits, '.', '_' or '-'.";
                return false;
            }

            if (!IsSafeVersion(
                    manifest.PackageVersion))
            {
                error =
                    "Shader package requires a non-empty package version using letters, digits, '.', '+', '_' or '-'.";
                return false;
            }

            manifest.ShaderIds ??=
                Array.Empty<string>();
            manifest.Textures ??=
                Array.Empty<ShaderPackageTextureResource>();
            manifest.PreviewFiles ??=
                Array.Empty<string>();

            if (manifest.ShaderIds.Length == 0)
            {
                error =
                    "Shader package must declare at least one shader id.";
                return false;
            }

            if ((!string.IsNullOrWhiteSpace(
                     manifest.UnityVersion) &&
                 !IsSafeVersion(
                     manifest.UnityVersion)) ||
                (!string.IsNullOrWhiteSpace(
                     manifest.UrpVersion) &&
                 !IsSafeVersion(
                     manifest.UrpVersion)))
            {
                error =
                    "Shader package Unity/URP compatibility versions contain unsupported characters.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    manifest.WindowsBundle) &&
                string.IsNullOrWhiteSpace(
                    manifest.MacOSBundle))
            {
                error =
                    "Shader package must declare at least one platform bundle.";
                return false;
            }

            var shaderIds =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var shaderId in
                     manifest.ShaderIds)
            {
                if (string.IsNullOrWhiteSpace(
                        shaderId))
                {
                    error =
                        "Shader package contains an empty shader id.";
                    return false;
                }

                if (!shaderIds.Add(
                        shaderId))
                {
                    error =
                        $"Duplicate shader id '{shaderId}'.";
                    return false;
                }
            }

            var resources =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (!ValidateResourcePath(
                    manifest.WindowsBundle,
                    "Windows bundle",
                    required: false,
                    BundleExtensions,
                    resources,
                    out error) ||
                !ValidateResourcePath(
                    manifest.MacOSBundle,
                    "macOS bundle",
                    required: false,
                    BundleExtensions,
                    resources,
                    out error) ||
                !ValidateResourcePath(
                    manifest.MaterialPreset,
                    "Material preset",
                    required: false,
                    JsonExtensions,
                    resources,
                    out error))
            {
                return false;
            }

            var textureIds =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var texture in
                     manifest.Textures)
            {
                if (texture == null ||
                    !IsSafeIdentifier(
                        texture.TextureId))
                {
                    error =
                        "Every package texture requires a safe texture id.";
                    return false;
                }

                if (!textureIds.Add(
                        texture.TextureId))
                {
                    error =
                        $"Duplicate package texture id '{texture.TextureId}'.";
                    return false;
                }

                if (!ValidateResourcePath(
                        texture.Path,
                        "Texture",
                        required: true,
                        ImageExtensions,
                        resources,
                        out error))
                {
                    return false;
                }
            }

            foreach (var path in
                     manifest.PreviewFiles)
            {
                if (!ValidateResourcePath(
                        path,
                        "Preview",
                        required: true,
                        ImageExtensions,
                        resources,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryGetBundlePath(
            ShaderPackageManifest manifest,
            string targetPlatform,
            out string relativePath,
            out string error)
        {
            relativePath = null;

            if (!TryValidate(
                    manifest,
                    out error))
            {
                return false;
            }

            if (string.Equals(
                    targetPlatform,
                    ShaderBundleTargetPlatform.WindowsX64,
                    StringComparison.Ordinal))
            {
                relativePath =
                    manifest.WindowsBundle;
            }
            else if (string.Equals(
                         targetPlatform,
                         ShaderBundleTargetPlatform.MacOS,
                         StringComparison.Ordinal))
            {
                relativePath =
                    manifest.MacOSBundle;
            }
            else
            {
                error =
                    $"Shader package target platform '{targetPlatform}' is unsupported.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    relativePath))
            {
                error =
                    $"Shader package '{manifest.PackageId}' does not include a bundle for '{targetPlatform}'.";
                return false;
            }

            return true;
        }

        public static bool IsSafeRelativeResourcePath(
            string path)
        {
            return ValidateResourcePath(
                path,
                "Resource",
                required: true,
                allowedExtensions: null,
                resources:
                    new HashSet<string>(
                        StringComparer.Ordinal),
                out _);
        }

        private static bool ValidateResourcePath(
            string path,
            string label,
            bool required,
            string[] allowedExtensions,
            HashSet<string> resources,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(
                    path))
            {
                if (!required)
                {
                    return true;
                }

                error =
                    $"{label} path is required.";
                return false;
            }

            if (path.Length > 512 ||
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
                error =
                    $"{label} path '{path}' is not a safe canonical relative path.";
                return false;
            }

            var segments =
                path.Split('/');

            foreach (var segment in
                     segments)
            {
                if (string.IsNullOrEmpty(
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
                    error =
                        $"{label} path '{path}' contains an unsafe path segment.";
                    return false;
                }
            }

            var extension =
                GetExtension(
                    path);

            if (ForbiddenExtensions.Contains(
                    extension))
            {
                error =
                    $"{label} path '{path}' uses forbidden executable or source extension '{extension}'.";
                return false;
            }

            if (allowedExtensions != null &&
                !ContainsExtension(
                    allowedExtensions,
                    extension))
            {
                error =
                    $"{label} path '{path}' uses unsupported extension '{extension}'.";
                return false;
            }

            if (resources != null &&
                !resources.Add(
                    path))
            {
                error =
                    $"Duplicate package resource path '{path}'.";
                return false;
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
                if (char.IsLetterOrDigit(ch) ||
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
                if (char.IsLetterOrDigit(ch) ||
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

        private static string GetExtension(
            string path)
        {
            var slash =
                path.LastIndexOf('/');
            var dot =
                path.LastIndexOf('.');

            if (dot < 0 ||
                dot < slash)
            {
                return string.Empty;
            }

            return path.Substring(
                dot);
        }

        private static bool ContainsExtension(
            string[] extensions,
            string extension)
        {
            foreach (var candidate in
                     extensions)
            {
                if (string.Equals(
                        candidate,
                        extension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
