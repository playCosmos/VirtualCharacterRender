using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Runtime.EventRuntime.Unity;

namespace VCR.Editor.P12
{
    internal sealed class P12EffectPresetInstallResult
    {
        public P12EffectPresetAsset Preset;
        public GameObject Instance;
        public string EffectId;
        public int ParticleSystemCount;
    }

    internal static class P12EffectPresetUtility
    {
        public static bool TryValidatePreset(
            P12EffectPresetAsset preset,
            out string error)
        {
            error = null;

            if (preset == null)
            {
                error =
                    "Effect preset asset is required.";
                return false;
            }

            if (preset.FormatVersion !=
                P12EffectPresetAsset.CurrentFormatVersion)
            {
                error =
                    $"Effect preset format {preset.FormatVersion} is unsupported; expected {P12EffectPresetAsset.CurrentFormatVersion}.";
                return false;
            }

            if (!IsSafeLogicalId(
                    preset.EffectId))
            {
                error =
                    "Effect preset requires a safe non-empty EffectId using letters, digits, '.', '_', or '-'.";
                return false;
            }

            if (preset.Prefab == null)
            {
                error =
                    $"Effect preset '{preset.EffectId}' requires a project prefab.";
                return false;
            }

            if (!EditorUtility.IsPersistent(
                    preset.Prefab) ||
                PrefabUtility.GetPrefabAssetType(
                    preset.Prefab) ==
                    PrefabAssetType.NotAPrefab)
            {
                error =
                    $"Effect preset '{preset.EffectId}' must reference a persistent prefab asset, not a scene object.";
                return false;
            }

            var systems =
                preset.Prefab
                    .GetComponentsInChildren<
                        ParticleSystem>(
                        includeInactive:
                            true);

            if (systems == null ||
                systems.Length == 0)
            {
                error =
                    $"Effect preset '{preset.EffectId}' prefab requires at least one ParticleSystem.";
                return false;
            }

            foreach (var component in
                     preset.Prefab.GetComponentsInChildren<
                         Component>(
                         includeInactive:
                             true))
            {
                if (component == null)
                {
                    error =
                        $"Effect preset '{preset.EffectId}' prefab contains a missing component/script.";
                    return false;
                }

                if (component is Transform ||
                    component is ParticleSystem ||
                    component is ParticleSystemRenderer)
                {
                    continue;
                }

                error =
                    $"Effect preset '{preset.EffectId}' prefab contains unsupported component '{component.GetType().FullName}'. v1 permits only Transform, ParticleSystem, and ParticleSystemRenderer.";
                return false;
            }

            return true;
        }

        public static bool TryInstall(
            P12EffectPresetAsset preset,
            EffectEventActionHandler handler,
            out P12EffectPresetInstallResult result,
            out string error)
        {
            result = null;
            error = null;

            if (!TryValidatePreset(
                    preset,
                    out error))
            {
                return false;
            }

            if (handler == null)
            {
                error =
                    "Effect preset installation requires an EffectEventActionHandler.";
                return false;
            }

            var serialized =
                new SerializedObject(
                    handler);
            serialized.Update();

            var effects =
                serialized.FindProperty(
                    "effects");

            if (effects == null)
            {
                error =
                    "Effect handler serialized binding array is unavailable.";
                return false;
            }

            if (ContainsEffectId(
                    effects,
                    preset.EffectId))
            {
                error =
                    $"Effect id '{preset.EffectId}' is already registered.";
                return false;
            }

            var previousJson =
                EditorJsonUtility.ToJson(
                    handler,
                    prettyPrint:
                        false);
            GameObject instance =
                null;

            try
            {
                instance =
                    PrefabUtility.InstantiatePrefab(
                        preset.Prefab,
                        handler.gameObject.scene) as
                    GameObject;

                if (instance == null)
                {
                    error =
                        $"Effect preset '{preset.EffectId}' prefab could not be instantiated into the handler scene.";
                    return false;
                }

                Undo.RegisterCreatedObjectUndo(
                    instance,
                    "Install VCR Effect Preset");
                Undo.RecordObject(
                    handler,
                    "Install VCR Effect Preset");

                instance.name =
                    "VCR Effect - " +
                    preset.EffectId;
                instance.transform.SetParent(
                    handler.transform,
                    worldPositionStays:
                        false);

                if (preset.StartInactive)
                {
                    instance.SetActive(
                        false);
                }

                serialized.Update();
                effects =
                    serialized.FindProperty(
                        "effects");
                var index =
                    effects.arraySize;
                effects.arraySize =
                    index + 1;
                var binding =
                    effects.GetArrayElementAtIndex(
                        index);

                binding.FindPropertyRelative(
                        "EffectId")
                    .stringValue =
                        preset.EffectId;
                binding.FindPropertyRelative(
                        "Root")
                    .objectReferenceValue =
                        instance;

                var systems =
                    instance
                        .GetComponentsInChildren<
                            ParticleSystem>(
                            includeInactive:
                                true);
                var systemsProperty =
                    binding.FindPropertyRelative(
                        "ParticleSystems");
                systemsProperty.arraySize =
                    systems.Length;

                for (var i = 0;
                     i < systems.Length;
                     i++)
                {
                    systemsProperty
                        .GetArrayElementAtIndex(
                            i)
                        .objectReferenceValue =
                            systems[i];
                }

                binding.FindPropertyRelative(
                        "RestartOnPlay")
                    .boolValue =
                        preset.RestartOnPlay;
                binding.FindPropertyRelative(
                        "DeactivateOnStop")
                    .boolValue =
                        preset.DeactivateOnStop;

                serialized.ApplyModifiedProperties();

                if (!handler.RebuildBindings(
                        out error))
                {
                    EditorJsonUtility
                        .FromJsonOverwrite(
                            previousJson,
                            handler);
                    handler.RebuildBindings(
                        out _);

                    if (instance != null)
                    {
                        Undo.DestroyObjectImmediate(
                            instance);
                    }

                    return false;
                }

                EditorUtility.SetDirty(
                    handler);
                EditorSceneManager.MarkSceneDirty(
                    handler.gameObject.scene);

                result =
                    new P12EffectPresetInstallResult
                    {
                        Preset =
                            preset,
                        Instance =
                            instance,
                        EffectId =
                            preset.EffectId,
                        ParticleSystemCount =
                            systems.Length
                    };

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    EditorJsonUtility
                        .FromJsonOverwrite(
                            previousJson,
                            handler);
                    handler.RebuildBindings(
                        out _);
                }
                catch
                {
                }

                if (instance != null)
                {
                    try
                    {
                        Undo.DestroyObjectImmediate(
                            instance);
                    }
                    catch
                    {
                        UnityEngine.Object
                            .DestroyImmediate(
                                instance);
                    }
                }

                error =
                    "Effect preset installation failed: " +
                    exception.Message;
                return false;
            }
        }

        private static bool ContainsEffectId(
            SerializedProperty effects,
            string effectId)
        {
            for (var i = 0;
                 i < effects.arraySize;
                 i++)
            {
                var id =
                    effects
                        .GetArrayElementAtIndex(
                            i)
                        .FindPropertyRelative(
                            "EffectId")
                        ?.stringValue;

                if (string.Equals(
                        id,
                        effectId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSafeLogicalId(
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
    }
}
