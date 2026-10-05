using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Application;
using VCR.Runtime.Scene;

namespace VCR.Editor.P0
{
    public static class P0ConfigurationPersistenceValidation
    {
        private const long ConfigurationLimitBytes =
            4L * 1024L * 1024L;

        [MenuItem("VCR/P0/Validate Configuration Persistence")]
        public static void Validate()
        {
            var directory =
                Path.Combine(
                    Application.temporaryCachePath,
                    "vcr-p0-config-" +
                    Guid.NewGuid()
                        .ToString("N"));
            var path =
                Path.Combine(
                    directory,
                    "runtime.json");

            try
            {
                Directory.CreateDirectory(
                    directory);

                var store =
                    new RuntimeConfigurationStore(
                        path);
                var configuration =
                    SceneRuntimeConfiguration.Default;
                configuration.EnvironmentStateId =
                    "bounded-test";

                var pass =
                    store.TrySave(
                        configuration,
                        out var saveError) &&
                    string.IsNullOrEmpty(
                        saveError) &&
                    store.TryLoad(
                        out var loaded,
                        out var loadError) &&
                    string.IsNullOrEmpty(
                        loadError) &&
                    loaded.EnvironmentStateId ==
                        "bounded-test";

                var original =
                    pass
                        ? File.ReadAllText(
                            path)
                        : null;

                var oversized =
                    configuration;
                oversized.EnvironmentStateId =
                    new string(
                        'x',
                        checked(
                            (int)ConfigurationLimitBytes));

                var oversizedSaved =
                    store.TrySave(
                        oversized,
                        out var oversizedSaveError);

                pass =
                    pass &&
                    !oversizedSaved &&
                    !string.IsNullOrWhiteSpace(
                        oversizedSaveError) &&
                    File.Exists(
                        path) &&
                    File.ReadAllText(
                        path) ==
                        original &&
                    !File.Exists(
                        path + ".tmp");

                using (var stream =
                       new FileStream(
                           path,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.SetLength(
                        ConfigurationLimitBytes +
                        1);
                }

                var oversizedLoaded =
                    store.TryLoad(
                        out var rejectedConfiguration,
                        out var oversizedLoadError);

                pass =
                    pass &&
                    !oversizedLoaded &&
                    !string.IsNullOrWhiteSpace(
                        oversizedLoadError) &&
                    rejectedConfiguration
                        .EnvironmentStateId ==
                        SceneRuntimeConfiguration
                            .Default
                            .EnvironmentStateId;

                File.WriteAllBytes(
                    path,
                    new byte[]
                    {
                        0xff,
                        0xfe,
                        0xff
                    });

                var invalidUtf8Loaded =
                    store.TryLoad(
                        out _,
                        out var invalidUtf8Error);

                pass =
                    pass &&
                    !invalidUtf8Loaded &&
                    !string.IsNullOrWhiteSpace(
                        invalidUtf8Error);

                if (pass)
                {
                    Debug.Log(
                        "VCR P0 configuration persistence: PASS");
                }
                else
                {
                    Debug.LogError(
                        "VCR P0 configuration persistence: FAIL " +
                        $"save='{saveError}', load='{loadError}', " +
                        $"oversized-save='{oversizedSaveError}', " +
                        $"oversized-load='{oversizedLoadError}', " +
                        $"invalid-utf8='{invalidUtf8Error}'");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P0 configuration persistence: FAIL " +
                    exception);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(
                            directory))
                    {
                        Directory.Delete(
                            directory,
                            recursive: true);
                    }
                }
                catch
                {
                    // Validation cleanup must not hide the assertion.
                }
            }
        }
    }
}
