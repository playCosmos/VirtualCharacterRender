using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Appearance.Unity;

namespace VCR.Editor.P12
{
    internal static class P12AppearanceAuthoringUtility
    {
        public static bool TryDiscoverConvention(
            Transform characterRoot,
            string appearanceRootName,
            string outfitsRootName,
            string accessoriesRootName,
            out AppearanceOutfitBinding[] outfits,
            out AppearanceAccessoryBinding[] accessories,
            out string error)
        {
            outfits =
                Array.Empty<
                    AppearanceOutfitBinding>();
            accessories =
                Array.Empty<
                    AppearanceAccessoryBinding>();
            error = null;

            if (characterRoot == null)
            {
                error =
                    "Character root is required for appearance discovery.";
                return false;
            }

            var appearanceRoot =
                FindDirectChild(
                    characterRoot,
                    appearanceRootName);

            if (appearanceRoot == null)
            {
                error =
                    $"Appearance root '{appearanceRootName}' was not found under '{characterRoot.name}'.";
                return false;
            }

            var outfitContainer =
                FindDirectChild(
                    appearanceRoot,
                    outfitsRootName);
            var accessoryContainer =
                FindDirectChild(
                    appearanceRoot,
                    accessoriesRootName);
            var discoveredOutfits =
                new List<
                    AppearanceOutfitBinding>();
            var discoveredAccessories =
                new List<
                    AppearanceAccessoryBinding>();
            var outfitIds =
                new HashSet<string>(
                    StringComparer.Ordinal);
            var accessoryKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (outfitContainer != null)
            {
                for (var i = 0;
                     i < outfitContainer.childCount;
                     i++)
                {
                    var child =
                        outfitContainer.GetChild(
                            i);
                    var id =
                        NormalizeId(
                            child.name);

                    if (string.IsNullOrWhiteSpace(
                            id))
                    {
                        error =
                            "Discovered outfit contains an empty id.";
                        return false;
                    }

                    if (!outfitIds.Add(
                            id))
                    {
                        error =
                            $"Discovered outfit id '{id}' appears more than once.";
                        return false;
                    }

                    discoveredOutfits.Add(
                        new AppearanceOutfitBinding
                        {
                            OutfitId =
                                id,
                            Roots =
                                new[]
                                {
                                    child.gameObject
                                }
                        });
                }
            }

            if (accessoryContainer != null)
            {
                for (var slotIndex = 0;
                     slotIndex <
                     accessoryContainer.childCount;
                     slotIndex++)
                {
                    var slot =
                        accessoryContainer.GetChild(
                            slotIndex);
                    var slotId =
                        NormalizeId(
                            slot.name);

                    if (string.IsNullOrWhiteSpace(
                            slotId))
                    {
                        error =
                            "Discovered accessory slot contains an empty id.";
                        return false;
                    }

                    for (var itemIndex = 0;
                         itemIndex <
                         slot.childCount;
                         itemIndex++)
                    {
                        var item =
                            slot.GetChild(
                                itemIndex);
                        var accessoryId =
                            NormalizeId(
                                item.name);
                        var key =
                            slotId +
                            "\n" +
                            accessoryId;

                        if (string.IsNullOrWhiteSpace(
                                accessoryId))
                        {
                            error =
                                $"Accessory slot '{slotId}' contains an empty accessory id.";
                            return false;
                        }

                        if (!accessoryKeys.Add(
                                key))
                        {
                            error =
                                $"Discovered accessory '{slotId}/{accessoryId}' appears more than once.";
                            return false;
                        }

                        discoveredAccessories.Add(
                            new AppearanceAccessoryBinding
                            {
                                SlotId =
                                    slotId,
                                AccessoryId =
                                    accessoryId,
                                Root =
                                    item.gameObject
                            });
                    }
                }
            }

            outfits =
                discoveredOutfits.ToArray();
            accessories =
                discoveredAccessories.ToArray();

            if (outfits.Length == 0 &&
                accessories.Length == 0)
            {
                error =
                    $"Appearance hierarchy '{appearanceRoot.name}' contains no discoverable outfits or accessories.";
                return false;
            }

            return true;
        }

        public static AppearancePresetBinding
            CreatePresetFromCurrent(
                string presetId,
                AppearanceStateSnapshot current,
                string preferredTransitionId)
        {
            var selections =
                current.Accessories ??
                Array.Empty<
                    AppearanceAccessorySelection>();
            var bindings =
                new AppearanceAccessorySelectionBinding[
                    selections.Length];

            for (var i = 0;
                 i < selections.Length;
                 i++)
            {
                bindings[i] =
                    new AppearanceAccessorySelectionBinding
                    {
                        SlotId =
                            selections[i]?.SlotId,
                        AccessoryId =
                            selections[i]?.AccessoryId
                    };
            }

            return new AppearancePresetBinding
            {
                PresetId =
                    NormalizeId(
                        presetId),
                OutfitId =
                    current.OutfitId,
                PreferredTransitionId =
                    string.IsNullOrWhiteSpace(
                        preferredTransitionId)
                        ? "Immediate"
                        : preferredTransitionId.Trim(),
                Accessories =
                    bindings
            };
        }

        public static string BuildUniqueId(
            string preferred,
            Func<string, bool> exists)
        {
            var baseId =
                NormalizeId(
                    preferred);

            if (string.IsNullOrWhiteSpace(
                    baseId))
            {
                baseId =
                    "appearance";
            }

            var candidate =
                baseId;
            var suffix = 2;

            while (exists != null &&
                   exists(
                       candidate))
            {
                candidate =
                    baseId +
                    "-" +
                    suffix++;
            }

            return candidate;
        }

        private static Transform FindDirectChild(
            Transform parent,
            string name)
        {
            if (parent == null ||
                string.IsNullOrWhiteSpace(
                    name))
            {
                return null;
            }

            for (var i = 0;
                 i < parent.childCount;
                 i++)
            {
                var child =
                    parent.GetChild(
                        i);

                if (string.Equals(
                        child.name,
                        name,
                        StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static string NormalizeId(
            string value) =>
                value?.Trim();
    }
}
