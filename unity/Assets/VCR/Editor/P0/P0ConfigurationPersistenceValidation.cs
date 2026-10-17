using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Application;
using VCR.Runtime.Core;
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

                var invalidPathRejected =
                    !BoundedTextFile.TryWriteUtf8Atomic(
                        "invalid\0path",
                        "{}",
                        1024,
                        out var invalidPathError) &&
                    !string.IsNullOrWhiteSpace(
                        invalidPathError);

                var store =
                    new RuntimeConfigurationStore(
                        path);
                var configuration =
                    SceneRuntimeConfiguration.Default;
                configuration.EnvironmentStateId =
                    "bounded-test";

                var saved =
                    store.TrySave(
                        configuration,
                        out var saveError);

                var loaded =
                    SceneRuntimeConfiguration.Default;
                string loadError =
                    null;
                var loadedSuccessfully =
                    saved &&
                    store.TryLoad(
                        out loaded,
                        out loadError);

                var pass =
                    invalidPathRejected &&
                    saved &&
                    string.IsNullOrEmpty(
                        saveError) &&
                    loadedSuccessfully &&
                    string.IsNullOrEmpty(
                        loadError) &&
                    loaded.EnvironmentStateId ==
                        "bounded-test";

                File.WriteAllText(
                    path + ".bak",
                    "stale-backup");

                var staleBackupSaved =
                    store.TrySave(
                        configuration,
                        out var staleBackupError);

                pass =
                    pass &&
                    staleBackupSaved &&
                    string.IsNullOrEmpty(
                        staleBackupError) &&
                    File.Exists(
                        path + ".bak") &&
                    File.ReadAllText(
                        path + ".bak").Contains(
                            "bounded-test");

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
                        out var recoveredOversizedConfiguration,
                        out var oversizedLoadError);

                pass =
                    pass &&
                    oversizedLoaded &&
                    string.IsNullOrEmpty(
                        oversizedLoadError) &&
                    !string.IsNullOrWhiteSpace(
                        store.LastLoadWarning) &&
                    recoveredOversizedConfiguration
                        .EnvironmentStateId ==
                        "bounded-test" &&
                    new FileInfo(path).Length >
                        ConfigurationLimitBytes;

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
                        out var recoveredInvalidUtf8,
                        out var invalidUtf8Error);

                pass =
                    pass &&
                    invalidUtf8Loaded &&
                    string.IsNullOrEmpty(
                        invalidUtf8Error) &&
                    !string.IsNullOrWhiteSpace(
                        store.LastLoadWarning) &&
                    recoveredInvalidUtf8.EnvironmentStateId ==
                        "bounded-test" &&
                    File.ReadAllBytes(path)[0] == 0xff;

                // Invalid backup must not mask a corrupt primary.
                File.WriteAllBytes(
                    path + ".bak",
                    new byte[] { 0xff, 0xfe, 0xff });
                var invalidBothLoaded =
                    store.TryLoad(
                        out var rejectedBoth,
                        out var invalidBothError);
                pass =
                    pass &&
                    !invalidBothLoaded &&
                    !string.IsNullOrWhiteSpace(
                        invalidBothError) &&
                    string.IsNullOrWhiteSpace(
                        store.LastLoadWarning) &&
                    rejectedBoth.EnvironmentStateId ==
                        SceneRuntimeConfiguration.Default.EnvironmentStateId;

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
                        $"stale-backup-save='{staleBackupError}', " +
                        $"oversized-save='{oversizedSaveError}', " +
                        $"oversized-load='{oversizedLoadError}', " +
                        $"invalid-utf8='{invalidUtf8Error}', " +
                        $"invalid-both='{invalidBothError}'");
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
