using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Materials;
using VCR.Runtime.Materials.Unity;

namespace VCR.Editor.P7
{
    public static class P7ShaderPackageValidation
    {
        private const string ShaderAssetPath =
            "Assets/VCR/P0/Shaders/P0TintUnlit.shader";
        private const string ShaderId =
            "VCR/P0/TintUnlit";
        private const string TextureId =
            "vcr.p7.test.texture";

        [MenuItem("VCR/P7/Validate Shader Package Runtime")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            ValidateManifest(
                failures);
            ValidateTransactionalLoader(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P7 shader package validation: PASS " +
                    "(manifest security, platform routing, declarative resources, transactional registry rollback)");
                return true;
            }

            Debug.LogError(
                "VCR P7 shader package validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void ValidateManifest(
            List<string> failures)
        {
            var valid =
                CreateValidManifest();

            Expect(
                ShaderPackageManifestValidator
                    .TryValidate(
                        valid,
                        out var validError) &&
                string.IsNullOrEmpty(
                    validError),
                "valid declarative shader package manifest must validate",
                failures);

            Expect(
                ShaderPackageManifestValidator
                    .TryGetBundlePath(
                        valid,
                        ShaderBundleTargetPlatform
                            .WindowsX64,
                        out var windowsBundle,
                        out _) &&
                windowsBundle ==
                    "windows/shaders.bundle",
                "Windows package bundle path must resolve deterministically",
                failures);

            Expect(
                ShaderPackageManifestValidator
                    .TryGetBundlePath(
                        valid,
                        ShaderBundleTargetPlatform
                            .MacOS,
                        out var macBundle,
                        out _) &&
                macBundle ==
                    "macos/shaders.bundle",
                "macOS package bundle path must resolve deterministically",
                failures);

            var noShaders =
                CreateValidManifest();
            noShaders.ShaderIds =
                Array.Empty<string>();

            Expect(
                !ShaderPackageManifestValidator
                    .TryValidate(
                        noShaders,
                        out _),
                "shader package without declared shader ids must be rejected",
                failures);

            var duplicateShaders =
                CreateValidManifest();
            duplicateShaders.ShaderIds =
                new[]
                {
                    ShaderId,
                    ShaderId
                };

            Expect(
                !ShaderPackageManifestValidator
                    .TryValidate(
                        duplicateShaders,
                        out _),
                "duplicate shader ids must be rejected",
                failures);

            var duplicateTextures =
                CreateValidManifest();
            duplicateTextures.Textures =
                new[]
                {
                    new ShaderPackageTextureResource
                    {
                        TextureId = TextureId,
                        Path = "textures/a.png"
                    },
                    new ShaderPackageTextureResource
                    {
                        TextureId = TextureId,
                        Path = "textures/b.png"
                    }
                };

            Expect(
                !ShaderPackageManifestValidator
                    .TryValidate(
                        duplicateTextures,
                        out _),
                "duplicate texture ids must be rejected",
                failures);

            var duplicateResources =
                CreateValidManifest();
            duplicateResources.PreviewFiles =
                new[]
                {
                    "textures/test.png"
                };

            Expect(
                !ShaderPackageManifestValidator
                    .TryValidate(
                        duplicateResources,
                        out _),
                "duplicate resource paths across manifest sections must be rejected",
                failures);

            var invalidId =
                CreateValidManifest();
            invalidId.PackageId =
                "../unsafe";

            Expect(
                !ShaderPackageManifestValidator
                    .TryValidate(
                        invalidId,
                        out _),
                "unsafe package ids must be rejected",
                failures);

            var unsupportedFormat =
                CreateValidManifest();
            unsupportedFormat.FormatVersion =
                ShaderPackageManifest
                    .CurrentFormatVersion + 1;

            Expect(
                !ShaderPackageManifestValidator
                    .TryValidate(
                        unsupportedFormat,
                        out _),
                "unsupported manifest format versions must be rejected",
                failures);

            var missingMac =
                CreateValidManifest();
            missingMac.MacOSBundle =
                null;

            Expect(
                !ShaderPackageManifestValidator
                    .TryGetBundlePath(
                        missingMac,
                        ShaderBundleTargetPlatform.MacOS,
                        out _,
                        out var missingMacError) &&
                !string.IsNullOrEmpty(
                    missingMacError),
                "platform bundle lookup must reject a package that does not include the requested target",
                failures);

            Expect(
                !ShaderPackageManifestValidator
                    .IsSafeRelativeResourcePath(
                        "../escape.png") &&
                !ShaderPackageManifestValidator
                    .IsSafeRelativeResourcePath(
                        "textures\\escape.png") &&
                !ShaderPackageManifestValidator
                    .IsSafeRelativeResourcePath(
                        "plugins/unsafe.dll") &&
                !ShaderPackageManifestValidator
                    .IsSafeRelativeResourcePath(
                        "shaders/source.shader") &&
                !ShaderPackageManifestValidator
                    .IsSafeRelativeResourcePath(
                        "shaders/source.hlsl"),
                "resource validation must reject traversal, non-canonical separators, executable code, and raw shader source",
                failures);
        }

        private static void ValidateTransactionalLoader(
            List<string> failures)
        {
            if (!TryGetEditorTarget(
                    out var buildTarget,
                    out var targetPlatform,
                    out var platformFolder))
            {
                Debug.LogWarning(
                    "VCR P7 transaction validation: current Editor platform is outside the supported Windows/macOS product targets; manifest-only validation was executed.");
                return;
            }

            var shader =
                AssetDatabase.LoadAssetAtPath<
                    Shader>(
                    ShaderAssetPath);

            if (shader == null)
            {
                failures.Add(
                    "P7 validation shader asset is missing.");
                return;
            }

            var rootPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "vcr-p7-package-" +
                    Guid.NewGuid()
                        .ToString("N"));

            GameObject host = null;
            Texture2D sentinelTexture = null;

            var shaderSnapshot =
                RuntimeShaderRegistry
                    .CaptureRegistered();
            var textureSnapshot =
                RuntimeTextureRegistry
                    .CaptureRegistered();

            try
            {
                Directory.CreateDirectory(
                    rootPath);

                var bundleDirectory =
                    Path.Combine(
                        rootPath,
                        platformFolder);

                Directory.CreateDirectory(
                    bundleDirectory);

                var builds =
                    new[]
                    {
                        new AssetBundleBuild
                        {
                            assetBundleName =
                                "shaders.bundle",
                            assetNames =
                                new[]
                                {
                                    ShaderAssetPath
                                }
                        }
                    };

                var buildManifest =
                    BuildPipeline.BuildAssetBundles(
                        bundleDirectory,
                        builds,
                        BuildAssetBundleOptions
                            .ForceRebuildAssetBundle,
                        buildTarget);

                if (buildManifest == null)
                {
                    failures.Add(
                        "P7 validation could not build the temporary shader AssetBundle.");
                    return;
                }

                var bundleRelative =
                    platformFolder +
                    "/shaders.bundle";
                var bundlePath =
                    Path.Combine(
                        bundleDirectory,
                        "shaders.bundle");

                WriteTexture(
                    rootPath);
                WritePreset(
                    rootPath);

                var packageManifest =
                    new ShaderPackageManifest
                    {
                        PackageId =
                            "vcr.p7.validation",
                        PackageVersion =
                            "1.0.0",
                        UnityVersion =
                            Application.unityVersion,
                        UrpVersion =
                            "17.3.0",
                        WindowsBundle =
                            targetPlatform ==
                            ShaderBundleTargetPlatform
                                .WindowsX64
                                ? bundleRelative
                                : null,
                        MacOSBundle =
                            targetPlatform ==
                            ShaderBundleTargetPlatform
                                .MacOS
                                ? bundleRelative
                                : null,
                        MaterialPreset =
                            "defaults/materials.json",
                        ShaderIds =
                            new[]
                            {
                                ShaderId
                            },
                        Textures =
                            new[]
                            {
                                new ShaderPackageTextureResource
                                {
                                    TextureId =
                                        TextureId,
                                    Path =
                                        "textures/test.png"
                                }
                            }
                    };

                File.WriteAllText(
                    Path.Combine(
                        rootPath,
                        "manifest.json"),
                    JsonUtility.ToJson(
                        packageManifest,
                        prettyPrint: true));

                sentinelTexture =
                    new Texture2D(
                        1,
                        1,
                        TextureFormat.RGBA32,
                        mipChain: false);

                RuntimeShaderRegistry.Register(
                    ShaderId,
                    shader);
                RuntimeTextureRegistry.Register(
                    TextureId,
                    sentinelTexture);

                host =
                    new GameObject(
                        "P7 Shader Package Validation");

                var loader =
                    host.AddComponent<
                        RuntimeShaderPackageLoader>();

                var loaded =
                    loader.TryLoadPackage(
                        rootPath,
                        out var loadError);

                Expect(
                    loaded &&
                    string.IsNullOrEmpty(
                        loadError) &&
                    loader.ActivePackageId ==
                        "vcr.p7.validation" &&
                    loader.Status.Success,
                    "temporary declarative package must load successfully",
                    failures);

                Expect(
                    RuntimeShaderRegistry
                        .TryGetRegistered(
                            ShaderId,
                            out var packageShader) &&
                    packageShader != null,
                    "package shader must be registered",
                    failures);

                Expect(
                    RuntimeTextureRegistry
                        .TryGetRegistered(
                            TextureId,
                            out var packageTexture) &&
                    packageTexture != null &&
                    !ReferenceEquals(
                        packageTexture,
                        sentinelTexture),
                    "package texture must replace the underlying registry value while active",
                    failures);

                Expect(
                    loader.LoadedPresetDocument !=
                        null &&
                    loader.LoadedPresetDocument
                        .Presets.Length == 1 &&
                    loader.LoadedPresetDocument
                        .Presets[0].PresetId ==
                        "p7.validation.default",
                    "package material preset must be validated and exposed",
                    failures);

                File.WriteAllBytes(
                    bundlePath,
                    new byte[]
                    {
                        0x56,
                        0x43,
                        0x52,
                        0x37
                    });

                var reloadSucceeded =
                    loader.TryReloadActive(
                        out var reloadError);

                Expect(
                    !reloadSucceeded &&
                    !string.IsNullOrEmpty(
                        reloadError) &&
                    loader.ActivePackageId ==
                        "vcr.p7.validation",
                    "failed hot reload must retain the previous active package identity",
                    failures);

                Expect(
                    RuntimeShaderRegistry
                        .TryGetRegistered(
                            ShaderId,
                            out var rolledBackShader) &&
                    ReferenceEquals(
                        rolledBackShader,
                        packageShader) &&
                    RuntimeTextureRegistry
                        .TryGetRegistered(
                            TextureId,
                            out var rolledBackTexture) &&
                    ReferenceEquals(
                        rolledBackTexture,
                        packageTexture),
                    "failed hot reload must atomically restore active shader/texture registry state",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                loader.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "shader_package.load_attempts",
                        out var attempts) &&
                    Math.Abs(
                        attempts - 2.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "shader_package.load_successes",
                        out var successes) &&
                    Math.Abs(
                        successes - 1.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "shader_package.load_failures",
                        out var loadFailures) &&
                    Math.Abs(
                        loadFailures - 1.0) <
                    0.001,
                    "package diagnostics must expose successful and failed transactional load attempts",
                    failures);

                loader.UnloadActivePackage();

                Expect(
                    RuntimeShaderRegistry
                        .TryGetRegistered(
                            ShaderId,
                            out var restoredShader) &&
                    ReferenceEquals(
                        restoredShader,
                        shader) &&
                    RuntimeTextureRegistry
                        .TryGetRegistered(
                            TextureId,
                            out var restoredTexture) &&
                    ReferenceEquals(
                        restoredTexture,
                        sentinelTexture),
                    "unloading a package must restore underlying registry values it shadowed",
                    failures);

                var forbiddenPath =
                    Path.Combine(
                        rootPath,
                        "undeclared.dll");
                File.WriteAllBytes(
                    forbiddenPath,
                    new byte[]
                    {
                        0
                    });

                Expect(
                    !loader.TryLoadPackage(
                        rootPath,
                        out var forbiddenError) &&
                    !string.IsNullOrEmpty(
                        forbiddenError),
                    "undeclared executable files anywhere in the package inventory must reject the package before loading",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected P7 transaction validation exception: " +
                    exception);
            }
            finally
            {
                RuntimeShaderRegistry
                    .RestoreRegistered(
                        shaderSnapshot);
                RuntimeTextureRegistry
                    .RestoreRegistered(
                        textureSnapshot);

                if (host != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            host);
                }

                if (sentinelTexture != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            sentinelTexture);
                }

                try
                {
                    if (Directory.Exists(
                            rootPath))
                    {
                        Directory.Delete(
                            rootPath,
                            recursive: true);
                    }
                }
                catch
                {
                    // Temporary validation cleanup only.
                }
            }
        }

        private static ShaderPackageManifest
            CreateValidManifest()
        {
            return new ShaderPackageManifest
            {
                PackageId =
                    "vcr.test.shader-package",
                PackageVersion =
                    "1.0.0",
                UnityVersion =
                    Application.unityVersion,
                UrpVersion =
                    "17.3.0",
                WindowsBundle =
                    "windows/shaders.bundle",
                MacOSBundle =
                    "macos/shaders.bundle",
                MaterialPreset =
                    "defaults/materials.json",
                ShaderIds =
                    new[]
                    {
                        ShaderId
                    },
                Textures =
                    new[]
                    {
                        new ShaderPackageTextureResource
                        {
                            TextureId =
                                TextureId,
                            Path =
                                "textures/test.png"
                        }
                    },
                PreviewFiles =
                    new[]
                    {
                        "preview/test.png"
                    }
            };
        }

        private static void WriteTexture(
            string rootPath)
        {
            var directory =
                Path.Combine(
                    rootPath,
                    "textures");

            Directory.CreateDirectory(
                directory);

            var texture =
                new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    mipChain: false);

            try
            {
                texture.SetPixels(
                    new[]
                    {
                        Color.white,
                        Color.black,
                        Color.red,
                        Color.blue
                    });
                texture.Apply();

                File.WriteAllBytes(
                    Path.Combine(
                        directory,
                        "test.png"),
                    texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object
                    .DestroyImmediate(
                        texture);
            }
        }

        private static void WritePreset(
            string rootPath)
        {
            var directory =
                Path.Combine(
                    rootPath,
                    "defaults");

            Directory.CreateDirectory(
                directory);

            var document =
                new MaterialPresetDocument
                {
                    Version =
                        MaterialPresetStore
                            .CurrentVersion,
                    Presets =
                        new[]
                        {
                            new MaterialOverridePreset
                            {
                                PresetId =
                                    "p7.validation.default",
                                ShaderId =
                                    ShaderId,
                                Parameters =
                                    Array.Empty<
                                        MaterialParameterOverride>()
                            }
                        }
                };

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "materials.json"),
                JsonUtility.ToJson(
                    document,
                    prettyPrint: true));
        }

        private static bool TryGetEditorTarget(
            out BuildTarget buildTarget,
            out string targetPlatform,
            out string platformFolder)
        {
#if UNITY_EDITOR_WIN
            buildTarget =
                BuildTarget.StandaloneWindows64;
            targetPlatform =
                ShaderBundleTargetPlatform
                    .WindowsX64;
            platformFolder =
                "windows";
            return true;
#elif UNITY_EDITOR_OSX
            buildTarget =
                BuildTarget.StandaloneOSX;
            targetPlatform =
                ShaderBundleTargetPlatform
                    .MacOS;
            platformFolder =
                "macos";
            return true;
#else
            buildTarget = default;
            targetPlatform = null;
            platformFolder = null;
            return false;
#endif
        }

        private static bool TryGetMetric(
            List<RuntimeMetric> metrics,
            string name,
            out double value)
        {
            foreach (var metric in metrics)
            {
                if (string.Equals(
                    metric.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    value = metric.Value;
                    return true;
                }
            }

            value = 0.0;
            return false;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }
}
