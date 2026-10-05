using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Capabilities;
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
            ValidateBoundedFileInputs(
                failures);
            ValidateDeclarativeCapabilityCatalog(
                failures);
            ValidateTransactionalLoader(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P7 shader package validation: PASS " +
                    "(manifest security, bounded binary/metadata input, declarative capability registration, platform routing, declarative resources, transactional registry rollback)");
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

        private static void ValidateBoundedFileInputs(
            List<string> failures)
        {
            var rootPath =
                Path.Combine(
                    Application.temporaryCachePath,
                    "vcr-p7-input-bounds-" +
                    Guid.NewGuid()
                        .ToString("N"));
            GameObject host =
                null;

            try
            {
                Directory.CreateDirectory(
                    rootPath);

                var binaryPath =
                    Path.Combine(
                        rootPath,
                        "bounded.bin");
                File.WriteAllBytes(
                    binaryPath,
                    new byte[]
                    {
                        0x01,
                        0x02
                    });

                Expect(
                    BoundedBinaryFile.TryRead(
                        binaryPath,
                        maxBytes: 2,
                        out var exactBytes,
                        out var exactError) &&
                    exactBytes.Length == 2 &&
                    string.IsNullOrEmpty(
                        exactError),
                    "bounded binary reader must accept a file at the exact configured limit",
                    failures);

                Expect(
                    !BoundedBinaryFile.TryRead(
                        binaryPath,
                        maxBytes: 1,
                        out _,
                        out var binaryLimitError) &&
                    !string.IsNullOrWhiteSpace(
                        binaryLimitError),
                    "bounded binary reader must reject a file larger than its allocation limit",
                    failures);

                var bundlePath =
                    Path.Combine(
                        rootPath,
                        "shader.bundle");
                File.WriteAllBytes(
                    bundlePath,
                    new byte[]
                    {
                        0x00
                    });

                var metadataPath =
                    bundlePath +
                    ".vcr.json";

                using (var stream =
                       new FileStream(
                           metadataPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.SetLength(
                        1L * 1024L * 1024L +
                        1L);
                }

                host =
                    new GameObject(
                        "P7 Shader Bundle Input Bounds");
                var loader =
                    host.AddComponent<
                        RuntimeShaderBundleLoader>();

                Expect(
                    !loader.TryLoadFromFile(
                        bundlePath,
                        out var registeredCount,
                        out var metadataLimitError) &&
                    registeredCount == 0 &&
                    !string.IsNullOrWhiteSpace(
                        metadataLimitError),
                    "standalone shader bundle loader must reject oversized metadata before AssetBundle loading",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "bounded shader input validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        host);
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
                    // Validation cleanup must not hide the assertion.
                }
            }
        }

        private static void ValidateDeclarativeCapabilityCatalog(
            List<string> failures)
        {
            var catalog =
                new DeclarativeCapabilityCatalog();
            var ownerA =
                new object();
            var ownerB =
                new object();

            var registered =
                catalog.TryReplace(
                    ownerA,
                    "vcr.p7.catalog-a",
                    "1.0.0",
                    new[]
                    {
                        CapabilityIds
                            .RenderCustomShader
                    },
                    out var registerError);

            Expect(
                registered &&
                string.IsNullOrEmpty(
                    registerError) &&
                catalog.RegisteredProviderCount == 1 &&
                catalog.GetProviderCount(
                    CapabilityIds
                        .RenderCustomShader) == 1,
                "declarative capability catalog must register metadata without creating an executable service",
                failures);

            Expect(
                !catalog.TryReplace(
                    ownerB,
                    "vcr.p7.catalog-a",
                    "2.0.0",
                    new[]
                    {
                        CapabilityIds
                            .RenderCustomShader
                    },
                    out var conflictError) &&
                !string.IsNullOrEmpty(
                    conflictError),
                "declarative capability provider id ownership must reject a second owner",
                failures);

            var snapshot =
                catalog.CaptureOwner(
                    ownerA);

            Expect(
                snapshot != null &&
                snapshot.ProviderId ==
                    "vcr.p7.catalog-a" &&
                snapshot.ProviderVersion ==
                    "1.0.0" &&
                ContainsCapability(
                    snapshot,
                    CapabilityIds
                        .RenderCustomShader),
                "declarative capability metadata snapshot must preserve provider identity, version, and capability ids",
                failures);

            Expect(
                catalog.TryReplace(
                    ownerA,
                    "vcr.p7.catalog-b",
                    "2.0.0",
                    new[]
                    {
                        CapabilityIds
                            .RenderCustomShader
                    },
                    out var replaceError) &&
                string.IsNullOrEmpty(
                    replaceError) &&
                !catalog.TryGetProvider(
                    "vcr.p7.catalog-a",
                    out _) &&
                catalog.TryGetProvider(
                    "vcr.p7.catalog-b",
                    out _),
                "one owner must replace its declarative provider registration atomically",
                failures);

            Expect(
                catalog.RestoreOwner(
                    ownerA,
                    snapshot,
                    out var restoreError) &&
                string.IsNullOrEmpty(
                    restoreError) &&
                catalog.TryGetProvider(
                    "vcr.p7.catalog-a",
                    out _) &&
                !catalog.TryGetProvider(
                    "vcr.p7.catalog-b",
                    out _),
                "declarative capability snapshot restore must support package rollback",
                failures);

            Expect(
                catalog.UnregisterOwner(
                    ownerA) &&
                catalog.RegisteredProviderCount == 0 &&
                !catalog.IsProvided(
                    CapabilityIds
                        .RenderCustomShader),
                "unregistering a declarative package owner must remove only metadata registration",
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

            var capabilityCatalog =
                DeclarativeCapabilityCatalog.Shared;
            var baselineCapabilityProviders =
                capabilityCatalog.GetProviderCount(
                    CapabilityIds.RenderCustomShader);

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
                    capabilityCatalog.GetProviderCount(
                        CapabilityIds.RenderCustomShader) ==
                        baselineCapabilityProviders + 1 &&
                    capabilityCatalog.TryGetProvider(
                        "vcr.p7.validation",
                        out var capabilityRegistration) &&
                    capabilityRegistration.ProviderVersion ==
                        "1.0.0" &&
                    ContainsCapability(
                        capabilityRegistration,
                        CapabilityIds.RenderCustomShader),
                    "successful shader package load must register its declarative custom-shader capability",
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
                    capabilityCatalog.GetProviderCount(
                        CapabilityIds.RenderCustomShader) ==
                        baselineCapabilityProviders + 1 &&
                    capabilityCatalog.TryGetProvider(
                        "vcr.p7.validation",
                        out var rolledBackCapability) &&
                    rolledBackCapability.ProviderVersion ==
                        "1.0.0",
                    "failed hot reload must preserve the previous declarative capability registration",
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
                    capabilityCatalog.GetProviderCount(
                        CapabilityIds.RenderCustomShader) ==
                        baselineCapabilityProviders &&
                    !capabilityCatalog.TryGetProvider(
                        "vcr.p7.validation",
                        out _),
                    "unloading a shader package must unregister its declarative capability metadata",
                    failures);

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

        private static bool ContainsCapability(
            DeclarativeCapabilityRegistration registration,
            string capabilityId)
        {
            if (registration == null ||
                string.IsNullOrWhiteSpace(
                    capabilityId))
            {
                return false;
            }

            foreach (var candidate in
                     registration.CapabilityIds)
            {
                if (string.Equals(
                        candidate,
                        capabilityId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
