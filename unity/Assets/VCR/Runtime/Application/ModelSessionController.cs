using System;
using System.IO;
using UnityEngine;

namespace VCR.Runtime.Application
{
    // Persistence decisions live outside both the legacy and Toolkit UI.
    // This is deliberately not a model library or an FBX runtime adapter.
    public enum RememberedModelRestoreDecision
    {
        NoPreference,
        ExplicitStartupModel,
        AlreadyActive,
        UnsupportedFormat,
        MissingFile,
        RestoreVrm
    }

    public static class ModelSessionController
    {
        // Preserve the existing v1 key until a separately reviewed migration.
        public const string PreferenceKey =
            ApplicationRuntimeBootstrap.LastCharacterPathPreferenceKey;

        public static string RememberedPath =>
            PlayerPrefs.GetString(PreferenceKey, string.Empty);

        public static RememberedModelRestoreDecision EvaluateRestore(
            string rememberedPath, bool explicitStartupModel,
            bool alreadyActive, bool fileExists)
        {
            // A failed explicit --vcr-vrm must never fall back silently.
            if (explicitStartupModel)
                return RememberedModelRestoreDecision.ExplicitStartupModel;
            if (alreadyActive)
                return RememberedModelRestoreDecision.AlreadyActive;
            if (string.IsNullOrWhiteSpace(rememberedPath))
                return RememberedModelRestoreDecision.NoPreference;
            if (!string.Equals(Path.GetExtension(rememberedPath), ".vrm",
                    StringComparison.OrdinalIgnoreCase))
                return RememberedModelRestoreDecision.UnsupportedFormat;
            return fileExists
                ? RememberedModelRestoreDecision.RestoreVrm
                : RememberedModelRestoreDecision.MissingFile;
        }

        public static RememberedModelRestoreDecision SelectStartupRestore(
            bool explicitStartupModel, bool alreadyActive,
            out string rememberedPath)
        {
            rememberedPath = RememberedPath;
            // Do not probe files if a CLI selection or live model takes priority.
            var exists = !explicitStartupModel && !alreadyActive &&
                !string.IsNullOrWhiteSpace(rememberedPath) &&
                File.Exists(rememberedPath);
            return EvaluateRestore(rememberedPath, explicitStartupModel,
                alreadyActive, exists);
        }

        public static void RememberSuccessfulManualLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(Path.GetExtension(path), ".vrm",
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Only a successfully loaded VRM path may be remembered.",
                    nameof(path));
            // Caller must already have confirmed a non-null runtime model.
            // Neither attempted/failed loads nor CLI loads write this key.
            PlayerPrefs.SetString(PreferenceKey, path);
            PlayerPrefs.Save();
        }

        public static void ForgetOnExplicitUnload()
        {
            PlayerPrefs.DeleteKey(PreferenceKey);
            PlayerPrefs.Save();
        }
    }
}
