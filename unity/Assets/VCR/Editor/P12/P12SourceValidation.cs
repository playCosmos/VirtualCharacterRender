using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;

namespace VCR.Editor.P12
{
    internal static class P12SourceValidation
    {
        [MenuItem("VCR/P12/Run Source Validation")]
        private static void RunFromMenu()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            RunAppearanceAuthoringChecks(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P12 source validation: PASS " +
                    "(appearance convention discovery, explicit wardrobe/accessory bindings, authored preset capture, duplicate-id rejection, serialized authoring contract)");
                return true;
            }

            Debug.LogError(
                "VCR P12 source validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void RunAppearanceAuthoringChecks(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P12 Appearance Validation");

                var appearance =
                    CreateChild(
                        root.transform,
                        "VCRAppearance");
                var outfitsRoot =
                    CreateChild(
                        appearance,
                        "Outfits");
                var accessoriesRoot =
                    CreateChild(
                        appearance,
                        "Accessories");
                var casual =
                    CreateChild(
                        outfitsRoot,
                        "Casual");
                CreateChild(
                    outfitsRoot,
                    "Formal");

                var headSlot =
                    CreateChild(
                        accessoriesRoot,
                        "Head");
                var hat =
                    CreateChild(
                        headSlot,
                        "Hat");
                CreateChild(
                    headSlot,
                    "Glasses");

                var runtime =
                    root.AddComponent<
                        BasicCharacterAppearanceRuntime>();

                Expect(
                    P12AppearanceAuthoringUtility
                        .TryDiscoverConvention(
                            root.transform,
                            "VCRAppearance",
                            "Outfits",
                            "Accessories",
                            out var outfits,
                            out var accessories,
                            out var discoveryError) &&
                    outfits.Length ==
                        2 &&
                    accessories.Length ==
                        2 &&
                    outfits[0].Roots.Length ==
                        1 &&
                    outfits[0].Roots[0] !=
                        null,
                    "P12 appearance convention discovery must produce explicit outfit/accessory bindings: " +
                    discoveryError,
                    failures);

                var preset =
                    new AppearancePresetBinding
                    {
                        PresetId =
                            "casual-hat",
                        OutfitId =
                            "Casual",
                        PreferredTransitionId =
                            "Immediate",
                        Accessories =
                            new[]
                            {
                                new AppearanceAccessorySelectionBinding
                                {
                                    SlotId =
                                        "Head",
                                    AccessoryId =
                                        "Hat"
                                }
                            }
                    };

                runtime.ConfigureBindings(
                    outfits,
                    accessories,
                    new[]
                    {
                        preset
                    },
                    Array.Empty<
                        AppearanceTransitionBinding>(),
                    Array.Empty<
                        MonoBehaviour>(),
                    "casual-hat");

                Expect(
                    runtime.Status.State ==
                        AppearanceRuntimeState.Ready,
                    "explicit discovered appearance bindings must rebuild into a ready runtime: " +
                    runtime.Status.LastError,
                    failures);

                Expect(
                    runtime.SetPreset(
                        "casual-hat",
                        "Immediate",
                        out var applyError) &&
                    runtime.Current.OutfitId ==
                        "Casual" &&
                    runtime.Current.Accessories.Length ==
                        1 &&
                    runtime.Current.Accessories[0]
                        .SlotId ==
                        "Head" &&
                    runtime.Current.Accessories[0]
                        .AccessoryId ==
                        "Hat",
                    "authored preset from explicit bindings must apply through the normal runtime path: " +
                    applyError,
                    failures);

                var captured =
                    P12AppearanceAuthoringUtility
                        .CreatePresetFromCurrent(
                            "captured-look",
                            runtime.Current,
                            "Immediate");

                Expect(
                    captured != null &&
                    captured.PresetId ==
                        "captured-look" &&
                    captured.OutfitId ==
                        "Casual" &&
                    captured.Accessories.Length ==
                        1 &&
                    captured.Accessories[0]
                        .SlotId ==
                        "Head" &&
                    captured.Accessories[0]
                        .AccessoryId ==
                        "Hat",
                    "P12 current-appearance capture must create an authored preset binding with outfit/accessory ids",
                    failures);

                var serialized =
                    new SerializedObject(
                        runtime);

                Expect(
                    serialized.FindProperty(
                        "outfits") !=
                        null &&
                    serialized.FindProperty(
                        "accessories") !=
                        null &&
                    serialized.FindProperty(
                        "presets") !=
                        null &&
                    serialized.FindProperty(
                        "defaultPresetId") !=
                        null &&
                    serialized.FindProperty(
                        "appearanceRootName") !=
                        null,
                    "P12 appearance authoring window serialized property contract must remain available",
                    failures);

                var duplicate =
                    CreateChild(
                        outfitsRoot,
                        "Casual");

                Expect(
                    !P12AppearanceAuthoringUtility
                        .TryDiscoverConvention(
                            root.transform,
                            "VCRAppearance",
                            "Outfits",
                            "Accessories",
                            out _,
                            out _,
                            out var duplicateError) &&
                    duplicateError != null &&
                    duplicateError.IndexOf(
                        "more than once",
                        StringComparison.OrdinalIgnoreCase) >=
                        0,
                    "P12 convention discovery must reject duplicate outfit ids before runtime binding",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    duplicate.gameObject);

                var existing =
                    new HashSet<string>(
                        StringComparer.Ordinal)
                    {
                        "Hat",
                        "Hat-2"
                    };
                var unique =
                    P12AppearanceAuthoringUtility
                        .BuildUniqueId(
                            "Hat",
                            existing.Contains);

                Expect(
                    unique ==
                        "Hat-3",
                    "P12 appearance authoring must generate stable suffix ids for duplicate logical names",
                    failures);

                Expect(
                    casual.gameObject.activeSelf &&
                    hat.gameObject.activeSelf,
                    "P12 validation hierarchy objects must remain usable scene roots after discovery",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "P12 appearance authoring validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            root);
                }
            }
        }

        private static Transform CreateChild(
            Transform parent,
            string name)
        {
            var child =
                new GameObject(
                    name)
                    .transform;
            child.SetParent(
                parent,
                false);
            return child;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(
                    message);
            }
        }
    }
}
