using System;
using System.Collections.Generic;
using VCR.Runtime.EventRuntime;

namespace VCR.Editor.P12
{
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
