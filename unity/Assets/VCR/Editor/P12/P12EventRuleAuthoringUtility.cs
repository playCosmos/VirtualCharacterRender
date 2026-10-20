using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Editor.P11;
using VCR.Runtime.EventRuntime;

namespace VCR.Editor.P12
{
    internal sealed class P12EventRuleGroupSummary
    {
        public string Group;
        public string[] RuleIds =
            Array.Empty<string>();
        public int EnabledCount;
    }

    internal static class P12EventRuleAuthoringUtility
    {
        public static bool TryValidateRules(
            EventRuntimeRule[] rules,
            out string error)
        {
            error = null;
            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule == null)
                {
                    error =
                        "Event graph contains a null rule.";
                    return false;
                }

                var id =
                    rule.Id?.Trim();

                if (string.IsNullOrWhiteSpace(
                        id))
                {
                    error =
                        "Every event rule requires a non-empty id.";
                    return false;
                }

                if (!ids.Add(
                        id))
                {
                    error =
                        $"Duplicate event rule id '{id}'.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(
                        rule.GraphGroup))
                {
                    if (!P11GraphGroupPathUtility
                        .TryNormalize(
                            rule.GraphGroup,
                            out var normalizedGroup,
                            out var groupError))
                    {
                        error =
                            $"Rule '{id}' graph group is invalid: {groupError}";
                        return false;
                    }

                    rule.GraphGroup =
                        normalizedGroup;
                }

                if (!IsFiniteNonNegative(
                        rule.CooldownSeconds))
                {
                    error =
                        $"Rule '{id}' cooldown must be finite and non-negative.";
                    return false;
                }

                if (!IsFiniteNonNegative(
                        rule.RateLimitWindowSeconds) ||
                    rule.RateLimitMaxExecutions < 0)
                {
                    error =
                        $"Rule '{id}' rate-limit values must be finite and non-negative.";
                    return false;
                }

                var hasRateWindow =
                    rule.RateLimitWindowSeconds >
                    0.0;
                var hasRateCount =
                    rule.RateLimitMaxExecutions >
                    0;

                if (hasRateWindow !=
                    hasRateCount)
                {
                    error =
                        $"Rule '{id}' rate limiting requires both a positive window and positive max execution count.";
                    return false;
                }

                if (!ValidateFilter(
                        id,
                        rule.Filter,
                        out error) ||
                    !ValidateConditions(
                        id,
                        rule.Conditions,
                        out error) ||
                    !ValidateMutations(
                        id,
                        rule.StateMutations,
                        out error) ||
                    !ValidateActions(
                        id,
                        rule.Actions,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        public static P12EventRuleGroupSummary[]
            CaptureGroupSummaries(
                EventRuntimeRule[] rules)
        {
            var groups =
                new Dictionary<
                    string,
                    List<EventRuntimeRule>>(
                    StringComparer.Ordinal);

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                var rawGroup =
                    rule?.GraphGroup;
                string group;

                if (string.IsNullOrWhiteSpace(
                        rawGroup) ||
                    !P11GraphGroupPathUtility
                        .TryNormalize(
                            rawGroup,
                            out group,
                            out _))
                {
                    continue;
                }

                if (!groups.TryGetValue(
                        group,
                        out var members))
                {
                    members =
                        new List<EventRuntimeRule>();
                    groups.Add(
                        group,
                        members);
                }

                members.Add(
                    rule);
            }

            var names =
                new List<string>(
                    groups.Keys);
            names.Sort(
                StringComparer.Ordinal);

            var result =
                new P12EventRuleGroupSummary[
                    names.Count];

            for (var i = 0;
                 i < names.Count;
                 i++)
            {
                var name =
                    names[i];
                var members =
                    groups[
                        name];
                var ids =
                    new string[
                        members.Count];
                var enabled = 0;

                for (var memberIndex = 0;
                     memberIndex <
                     members.Count;
                     memberIndex++)
                {
                    ids[
                        memberIndex] =
                            members[
                                memberIndex]
                                ?.Id ??
                            "<null>";

                    if (members[
                            memberIndex]
                            ?.Enabled ==
                        true)
                    {
                        enabled++;
                    }
                }

                result[i] =
                    new P12EventRuleGroupSummary
                    {
                        Group =
                            name,
                        RuleIds =
                            ids,
                        EnabledCount =
                            enabled
                    };
            }

            return result;
        }

        public static int FindAdjacentRuleIndexInGroup(
            EventRuntimeRule[] rules,
            int currentIndex,
            int direction)
        {
            rules ??=
                Array.Empty<EventRuntimeRule>();

            if (currentIndex < 0 ||
                currentIndex >=
                    rules.Length ||
                direction == 0)
            {
                return -1;
            }

            var rawGroup =
                rules[
                    currentIndex]
                    ?.GraphGroup;
            string group;

            if (string.IsNullOrWhiteSpace(
                    rawGroup) ||
                !P11GraphGroupPathUtility
                    .TryNormalize(
                        rawGroup,
                        out group,
                        out _))
            {
                return -1;
            }

            var step =
                direction < 0
                    ? -1
                    : 1;

            for (var offset = 1;
                 offset <=
                 rules.Length;
                 offset++)
            {
                var index =
                    (currentIndex +
                     step * offset +
                     rules.Length) %
                    rules.Length;

                if (index ==
                    currentIndex)
                {
                    break;
                }

                var candidateRaw =
                    rules[index]
                        ?.GraphGroup;

                if (P11GraphGroupPathUtility
                        .TryNormalize(
                            candidateRaw,
                            out var candidateGroup,
                            out _) &&
                    string.Equals(
                        candidateGroup,
                        group,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        public static int SetGroupEnabled(
            EventRuntimeRule[] rules,
            string group,
            bool enabled) =>
                SetGroupEnabled(
                    rules,
                    group,
                    enabled,
                    includeDescendants:
                        false);

        public static int SetGroupEnabled(
            EventRuntimeRule[] rules,
            string group,
            bool enabled,
            bool includeDescendants)
        {
            if (!P11GraphGroupPathUtility
                .TryNormalize(
                    group,
                    out var normalized,
                    out _))
            {
                return 0;
            }

            var changed = 0;

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule == null ||
                    !P11GraphGroupPathUtility
                        .Matches(
                            rule.GraphGroup,
                            normalized,
                            includeDescendants) ||
                    rule.Enabled ==
                        enabled)
                {
                    continue;
                }

                rule.Enabled =
                    enabled;
                changed++;
            }

            return changed;
        }

        public static string[] CaptureGroupPaths(
            EventRuntimeRule[] rules)
        {
            var groups =
                new List<string>();

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule != null &&
                    !string.IsNullOrWhiteSpace(
                        rule.GraphGroup))
                {
                    groups.Add(
                        rule.GraphGroup);
                }
            }

            return P11GraphGroupPathUtility
                .CaptureHierarchyPaths(
                    groups);
        }

        public static bool TryRewriteGroupHierarchy(
            EventRuntimeRule[] rules,
            string sourceGroupPath,
            string destinationGroupPath,
            bool includeDescendants,
            out int affectedRules,
            out string error)
        {
            affectedRules = 0;
            error = null;

            if (!P11GraphGroupPathUtility
                .TryNormalize(
                    sourceGroupPath,
                    out var source,
                    out error) ||
                !P11GraphGroupPathUtility
                    .TryNormalize(
                        destinationGroupPath,
                        out var destination,
                        out error))
            {
                return false;
            }

            if (string.Equals(
                    source,
                    destination,
                    StringComparison.Ordinal))
            {
                error =
                    "Source and destination rule group paths are identical.";
                return false;
            }

            if (includeDescendants &&
                destination.StartsWith(
                    source + "/",
                    StringComparison.Ordinal))
            {
                error =
                    "A rule group hierarchy cannot be moved inside itself.";
                return false;
            }

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule == null ||
                    !P11GraphGroupPathUtility
                        .TryRewrite(
                            rule.GraphGroup,
                            source,
                            destination,
                            includeDescendants,
                            out var rewritten))
                {
                    continue;
                }

                rule.GraphGroup =
                    rewritten;
                affectedRules++;
            }

            if (affectedRules == 0)
            {
                error =
                    $"Rule group path '{source}' is not assigned to any rule.";
                return false;
            }

            return true;
        }

        public static bool TryClearGroupHierarchy(
            EventRuntimeRule[] rules,
            string sourceGroupPath,
            bool includeDescendants,
            out int affectedRules,
            out string error)
        {
            affectedRules = 0;
            error = null;

            if (!P11GraphGroupPathUtility
                .TryNormalize(
                    sourceGroupPath,
                    out var source,
                    out error))
            {
                return false;
            }

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule == null ||
                    !P11GraphGroupPathUtility
                        .Matches(
                            rule.GraphGroup,
                            source,
                            includeDescendants))
                {
                    continue;
                }

                rule.GraphGroup =
                    string.Empty;
                affectedRules++;
            }

            if (affectedRules == 0)
            {
                error =
                    $"Rule group path '{source}' is not assigned to any rule.";
                return false;
            }

            return true;
        }

        public static EventRuntimeRule[]
            CaptureGroupHierarchyRules(
                EventRuntimeRule[] rules,
                string groupPath,
                bool includeDescendants)
        {
            if (!P11GraphGroupPathUtility
                .TryNormalize(
                    groupPath,
                    out var normalized,
                    out _))
            {
                return Array.Empty<
                    EventRuntimeRule>();
            }

            var result =
                new List<EventRuntimeRule>();

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule != null &&
                    P11GraphGroupPathUtility
                        .Matches(
                            rule.GraphGroup,
                            normalized,
                            includeDescendants))
                {
                    result.Add(
                        rule);
                }
            }

            return result.ToArray();
        }

        public static bool TryDuplicateGroupHierarchy(
            EventRuntimeRule[] rules,
            string sourceGroupPath,
            string destinationGroupPath,
            bool includeDescendants,
            out EventRuntimeRule[] duplicatedRules,
            out int duplicatedCount,
            out string error)
        {
            duplicatedRules =
                Array.Empty<
                    EventRuntimeRule>();
            duplicatedCount = 0;
            error = null;

            if (!P11GraphGroupPathUtility
                .TryNormalize(
                    sourceGroupPath,
                    out var source,
                    out error) ||
                !P11GraphGroupPathUtility
                    .TryNormalize(
                        destinationGroupPath,
                        out var destination,
                        out error))
            {
                return false;
            }

            var existing =
                new List<EventRuntimeRule>();
            var ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                var clone =
                    CloneRule(
                        rule);

                if (clone == null)
                {
                    error =
                        "Event rule hierarchy contains a null rule.";
                    return false;
                }

                existing.Add(
                    clone);

                if (!string.IsNullOrWhiteSpace(
                        clone.Id))
                {
                    ids.Add(
                        clone.Id);
                }
            }

            var additions =
                new List<EventRuntimeRule>();

            foreach (var rule in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (rule == null ||
                    !P11GraphGroupPathUtility
                        .Matches(
                            rule.GraphGroup,
                            source,
                            includeDescendants))
                {
                    continue;
                }

                var clone =
                    CloneRule(
                        rule);

                if (clone == null ||
                    !P11GraphGroupPathUtility
                        .TryRewrite(
                            rule.GraphGroup,
                            source,
                            destination,
                            includeDescendants,
                            out var rewrittenGroup))
                {
                    continue;
                }

                clone.Id =
                    BuildUniqueRuleId(
                        string.IsNullOrWhiteSpace(
                            clone.Id)
                            ? "event-rule-copy"
                            : clone.Id +
                              "-copy",
                        ids.Contains);
                ids.Add(
                    clone.Id);
                clone.GraphGroup =
                    rewrittenGroup;
                additions.Add(
                    clone);
            }

            if (additions.Count == 0)
            {
                error =
                    $"Rule group hierarchy '{source}' contains no rules to duplicate.";
                return false;
            }

            existing.AddRange(
                additions);

            var result =
                existing.ToArray();

            if (!TryValidateRules(
                    result,
                    out error))
            {
                return false;
            }

            duplicatedRules =
                result;
            duplicatedCount =
                additions.Count;
            return true;
        }

        public static string BuildUniqueRuleId(
            string preferred,
            Func<string, bool> exists)
        {
            var baseId =
                string.IsNullOrWhiteSpace(
                    preferred)
                    ? "event-rule"
                    : preferred.Trim();
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

        private static EventRuntimeRule CloneRule(
            EventRuntimeRule source)
        {
            if (source == null)
            {
                return null;
            }

            var json =
                JsonUtility.ToJson(
                    source);

            return JsonUtility.FromJson<
                EventRuntimeRule>(
                    json);
        }

        private static bool ValidateFilter(
            string ruleId,
            EventRuleFilter filter,
            out string error)
        {
            error = null;

            if (filter == null)
            {
                return true;
            }

            if (filter.HasMinimumAmount &&
                !IsFinite(
                    filter.MinimumAmount))
            {
                error =
                    $"Rule '{ruleId}' minimum amount must be finite.";
                return false;
            }

            if (filter.HasMaximumAmount &&
                !IsFinite(
                    filter.MaximumAmount))
            {
                error =
                    $"Rule '{ruleId}' maximum amount must be finite.";
                return false;
            }

            if (filter.HasMinimumAmount &&
                filter.HasMaximumAmount &&
                filter.MinimumAmount >
                    filter.MaximumAmount)
            {
                error =
                    $"Rule '{ruleId}' minimum amount cannot exceed maximum amount.";
                return false;
            }

            return true;
        }

        private static bool ValidateConditions(
            string ruleId,
            EventStateCondition[] conditions,
            out string error)
        {
            error = null;

            foreach (var condition in
                     conditions ??
                     Array.Empty<EventStateCondition>())
            {
                if (condition == null ||
                    string.IsNullOrWhiteSpace(
                        condition.Key))
                {
                    error =
                        $"Rule '{ruleId}' contains a condition without a state key.";
                    return false;
                }

                switch (condition.Kind)
                {
                    case EventStateConditionKind
                        .NumberGreaterOrEqual:
                    case EventStateConditionKind
                        .NumberLessOrEqual:
                    case EventStateConditionKind
                        .NumberEqual:
                        if (!IsFinite(
                                condition.NumberValue))
                        {
                            error =
                                $"Rule '{ruleId}' condition '{condition.Key}' uses a non-finite number.";
                            return false;
                        }

                        break;
                }
            }

            return true;
        }

        private static bool ValidateMutations(
            string ruleId,
            EventStateMutation[] mutations,
            out string error)
        {
            error = null;

            foreach (var mutation in
                     mutations ??
                     Array.Empty<EventStateMutation>())
            {
                if (mutation == null ||
                    string.IsNullOrWhiteSpace(
                        mutation.Key))
                {
                    error =
                        $"Rule '{ruleId}' contains a state mutation without a key.";
                    return false;
                }

                if (!IsFinite(
                        mutation.ConstantNumber) ||
                    !IsFinite(
                        mutation.NumericScale) ||
                    !IsFinite(
                        mutation.NumericOffset))
                {
                    error =
                        $"Rule '{ruleId}' mutation '{mutation.Key}' contains a non-finite numeric value.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateActions(
            string ruleId,
            EventActionTemplate[] actions,
            out string error)
        {
            error = null;

            foreach (var action in
                     actions ??
                     Array.Empty<EventActionTemplate>())
            {
                if (action == null ||
                    string.IsNullOrWhiteSpace(
                        action.ActionType))
                {
                    error =
                        $"Rule '{ruleId}' contains an action without ActionType.";
                    return false;
                }

                if (!IsFinite(
                        action.ConstantNumber) ||
                    !IsFinite(
                        action.ConstantNumberY) ||
                    !IsFinite(
                        action.ConstantNumberZ) ||
                    !IsFinite(
                        action.ConstantNumberW) ||
                    !IsFinite(
                        action.NumericScale) ||
                    !IsFinite(
                        action.NumericOffset))
                {
                    error =
                        $"Rule '{ruleId}' action '{action.ActionType}' contains a non-finite numeric value.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsFiniteNonNegative(
            double value) =>
                IsFinite(
                    value) &&
                value >= 0.0;

        private static bool IsFinite(
            double value) =>
                !double.IsNaN(
                    value) &&
                !double.IsInfinity(
                    value);
    }
}
