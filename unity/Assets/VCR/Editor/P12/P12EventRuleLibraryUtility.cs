using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.Events;

namespace VCR.Editor.P12
{
    internal enum P12BuiltInEventRuleTemplate
    {
        ManualRestoreDefault = 0,
        SubjectLostRestoreDefault = 1,
        ChatAppearancePreset = 2,
        DonationEffect = 3
    }

    [Serializable]
    internal sealed class P12EventRuleLibraryPackage
    {
        public const int CurrentVersion = 2;

        public int Version =
            CurrentVersion;
        public string PackageId;
        public string Description;
        public string[] Tags =
            Array.Empty<string>();
        public int Revision = 1;
        public EventRuntimeRule[] Rules =
            Array.Empty<EventRuntimeRule>();
    }

    internal static class P12EventRuleLibraryUtility
    {
        public static bool TryCreatePackage(
            string packageId,
            EventRuntimeRule[] rules,
            out P12EventRuleLibraryPackage package,
            out string error) =>
                TryCreatePackage(
                    packageId,
                    null,
                    Array.Empty<string>(),
                    1,
                    rules,
                    out package,
                    out error);

        public static bool TryCreatePackage(
            string packageId,
            string description,
            string[] tags,
            int revision,
            EventRuntimeRule[] rules,
            out P12EventRuleLibraryPackage package,
            out string error)
        {
            package = null;
            error = null;
            var id =
                packageId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    id))
            {
                error =
                    "Event rule library package id is required.";
                return false;
            }

            var cloned =
                CloneRules(
                    rules);

            if (!P12EventRuleAuthoringUtility
                .TryValidateRules(
                    cloned,
                    out error))
            {
                return false;
            }

            package =
                new P12EventRuleLibraryPackage
                {
                    PackageId =
                        id,
                    Description =
                        description,
                    Tags =
                        tags ??
                        Array.Empty<string>(),
                    Revision =
                        revision,
                    Rules =
                        cloned
                };

            return TryValidatePackage(
                package,
                out error);
        }

        public static bool TrySerialize(
            P12EventRuleLibraryPackage package,
            out string json,
            out string error)
        {
            json = null;
            error = null;

            if (!TryValidatePackage(
                    package,
                    out error))
            {
                return false;
            }

            try
            {
                json =
                    JsonUtility.ToJson(
                        package,
                        prettyPrint:
                            true);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Event rule library serialization failed: " +
                    exception.Message;
                return false;
            }
        }

        public static bool TryParse(
            string json,
            out P12EventRuleLibraryPackage package,
            out string error)
        {
            package = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                error =
                    "Event rule library JSON is empty.";
                return false;
            }

            try
            {
                package =
                    JsonUtility.FromJson<
                        P12EventRuleLibraryPackage>(
                        json);
            }
            catch (Exception exception)
            {
                error =
                    "Event rule library JSON parse failed: " +
                    exception.Message;
                return false;
            }

            return TryValidatePackage(
                package,
                out error);
        }

        public static bool TryValidatePackage(
            P12EventRuleLibraryPackage package,
            out string error)
        {
            error = null;

            if (package == null)
            {
                error =
                    "Event rule library package is missing.";
                return false;
            }

            if (package.Version >
                P12EventRuleLibraryPackage
                    .CurrentVersion)
            {
                error =
                    $"Event rule library version {package.Version} is newer than supported version {P12EventRuleLibraryPackage.CurrentVersion}.";
                return false;
            }

            if (package.Version < 1)
            {
                error =
                    $"Event rule library version {package.Version} is unsupported.";
                return false;
            }

            if (package.Version == 1)
            {
                package.Description =
                    string.Empty;
                package.Tags =
                    Array.Empty<string>();
                package.Revision = 1;
                package.Version =
                    P12EventRuleLibraryPackage
                        .CurrentVersion;
            }

            if (string.IsNullOrWhiteSpace(
                    package.PackageId))
            {
                error =
                    "Event rule library package id is required.";
                return false;
            }

            package.PackageId =
                package.PackageId.Trim();
            package.Description =
                package.Description?.Trim() ??
                string.Empty;

            if (package.Revision < 1)
            {
                error =
                    "Event rule library revision must be at least 1.";
                return false;
            }

            package.Tags ??=
                Array.Empty<string>();
            var normalizedTags =
                new string[
                    package.Tags.Length];
            var tagSet =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            for (var i = 0;
                 i < package.Tags.Length;
                 i++)
            {
                var tag =
                    package.Tags[i]?.Trim();

                if (string.IsNullOrWhiteSpace(
                        tag))
                {
                    error =
                        "Event rule library tags cannot be blank.";
                    return false;
                }

                if (!tagSet.Add(
                        tag))
                {
                    error =
                        $"Event rule library contains duplicate tag '{tag}'.";
                    return false;
                }

                normalizedTags[i] =
                    tag;
            }

            package.Tags =
                normalizedTags;
            package.Rules ??=
                Array.Empty<
                    EventRuntimeRule>();

            return P12EventRuleAuthoringUtility
                .TryValidateRules(
                    package.Rules,
                    out error);
        }

        public static EventRuntimeRule[] MergeRules(
            EventRuntimeRule[] existing,
            EventRuntimeRule[] incoming)
        {
            var result =
                new List<EventRuntimeRule>(
                    CloneRules(
                        existing));
            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var rule in result)
            {
                if (rule != null &&
                    !string.IsNullOrWhiteSpace(
                        rule.Id))
                {
                    ids.Add(
                        rule.Id);
                }
            }

            foreach (var source in
                     CloneRules(
                         incoming))
            {
                if (source == null)
                {
                    continue;
                }

                source.Id =
                    P12EventRuleAuthoringUtility
                        .BuildUniqueRuleId(
                            source.Id,
                            ids.Contains);
                ids.Add(
                    source.Id);
                result.Add(
                    source);
            }

            return result.ToArray();
        }

        public static EventRuntimeRule CreateTemplate(
            P12BuiltInEventRuleTemplate template)
        {
            switch (template)
            {
                case P12BuiltInEventRuleTemplate
                    .SubjectLostRestoreDefault:
                    return new EventRuntimeRule
                    {
                        Id =
                            "subject-lost-restore-default",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .TrackingSubjectLost
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .AppearanceRestoreDefault
                                }
                            }
                    };

                case P12BuiltInEventRuleTemplate
                    .ChatAppearancePreset:
                    return new EventRuntimeRule
                    {
                        Id =
                            "chat-appearance-preset",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .BroadcastChatMessage,
                                TextContains =
                                    "!look"
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .AppearanceSetPreset,
                                    TextSource =
                                        EventTextValueSource
                                            .Constant,
                                    ConstantText =
                                        "preset-id"
                                }
                            }
                    };

                case P12BuiltInEventRuleTemplate
                    .DonationEffect:
                    return new EventRuntimeRule
                    {
                        Id =
                            "donation-effect",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .BroadcastDonation,
                                RequireAmount =
                                    true,
                                HasMinimumAmount =
                                    true,
                                MinimumAmount =
                                    1.0
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .EffectPlay,
                                    TextSource =
                                        EventTextValueSource
                                            .Constant,
                                    ConstantText =
                                        "effect-id"
                                }
                            }
                    };

                default:
                    return new EventRuntimeRule
                    {
                        Id =
                            "manual-restore-default",
                        Filter =
                            new EventRuleFilter
                            {
                                Type =
                                    NormalizedEventTypes
                                        .LocalManual
                            },
                        Actions =
                            new[]
                            {
                                new EventActionTemplate
                                {
                                    ActionType =
                                        EventActionTypes
                                            .AppearanceRestoreDefault
                                }
                            }
                    };
            }
        }

        public static EventRuntimeRule[] CloneRules(
            EventRuntimeRule[] rules)
        {
            var wrapper =
                new P12EventRuleLibraryPackage
                {
                    PackageId =
                        "clone",
                    Rules =
                        rules ??
                        Array.Empty<
                            EventRuntimeRule>()
                };

            var json =
                JsonUtility.ToJson(
                    wrapper);
            var clone =
                JsonUtility.FromJson<
                    P12EventRuleLibraryPackage>(
                    json);

            return clone?.Rules ??
                   Array.Empty<
                       EventRuntimeRule>();
        }
    }
}
