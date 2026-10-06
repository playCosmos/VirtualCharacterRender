using System;

namespace VCR.Runtime.EventRuntime
{
    [Serializable]
    public sealed class EventRuntimeRule
    {
        public string Id;
        public string GraphLabel;
        public string GraphGroup;
        public bool Enabled = true;
        public EventRuleFilter Filter = new();
        public EventStateCondition[] Conditions =
            Array.Empty<EventStateCondition>();
        public EventStateMutation[] StateMutations =
            Array.Empty<EventStateMutation>();
        public EventActionTemplate[] Actions =
            Array.Empty<EventActionTemplate>();
        public double CooldownSeconds;
        public double RateLimitWindowSeconds;
        public int RateLimitMaxExecutions;
        public bool StopAfterMatch;
    }

    public static class EventRuntimeRuleCloner
    {
        public static EventRuntimeRule[] CloneRules(
            EventRuntimeRule[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    EventRuntimeRule>();
            }

            var result =
                new EventRuntimeRule[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                result[i] =
                    CloneRule(
                        source[i]);
            }

            return result;
        }

        public static EventRuntimeRule CloneRule(
            EventRuntimeRule source)
        {
            if (source == null)
            {
                return null;
            }

            return new EventRuntimeRule
            {
                Id = source.Id,
                GraphLabel = source.GraphLabel,
                GraphGroup = source.GraphGroup,
                Enabled = source.Enabled,
                Filter =
                    CloneFilter(
                        source.Filter),
                Conditions =
                    CloneConditions(
                        source.Conditions),
                StateMutations =
                    CloneMutations(
                        source.StateMutations),
                Actions =
                    CloneActions(
                        source.Actions),
                CooldownSeconds =
                    source.CooldownSeconds,
                RateLimitWindowSeconds =
                    source.RateLimitWindowSeconds,
                RateLimitMaxExecutions =
                    source.RateLimitMaxExecutions,
                StopAfterMatch =
                    source.StopAfterMatch
            };
        }

        private static EventRuleFilter CloneFilter(
            EventRuleFilter source)
        {
            if (source == null)
            {
                return null;
            }

            return new EventRuleFilter
            {
                Type = source.Type,
                SourceId = source.SourceId,
                ActorId = source.ActorId,
                TextContains =
                    source.TextContains,
                RequireAmount =
                    source.RequireAmount,
                HasMinimumAmount =
                    source.HasMinimumAmount,
                MinimumAmount =
                    source.MinimumAmount,
                HasMaximumAmount =
                    source.HasMaximumAmount,
                MaximumAmount =
                    source.MaximumAmount
            };
        }

        private static EventStateCondition[]
            CloneConditions(
                EventStateCondition[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    EventStateCondition>();
            }

            var result =
                new EventStateCondition[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var value =
                    source[i];

                result[i] =
                    value == null
                        ? null
                        : new EventStateCondition
                        {
                            Kind = value.Kind,
                            Key = value.Key,
                            NumberValue =
                                value.NumberValue,
                            TextValue =
                                value.TextValue
                        };
            }

            return result;
        }

        private static EventStateMutation[]
            CloneMutations(
                EventStateMutation[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    EventStateMutation>();
            }

            var result =
                new EventStateMutation[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var value =
                    source[i];

                result[i] =
                    value == null
                        ? null
                        : new EventStateMutation
                        {
                            Kind = value.Kind,
                            Key = value.Key,
                            NumericSource =
                                value.NumericSource,
                            ConstantNumber =
                                value.ConstantNumber,
                            NumericScale =
                                value.NumericScale,
                            NumericOffset =
                                value.NumericOffset,
                            TextSource =
                                value.TextSource,
                            ConstantText =
                                value.ConstantText,
                            TextTransforms =
                                value.TextTransforms,
                            TextPrefix =
                                value.TextPrefix,
                            TextSuffix =
                                value.TextSuffix
                        };
            }

            return result;
        }

        private static EventActionTemplate[]
            CloneActions(
                EventActionTemplate[] source)
        {
            if (source == null ||
                source.Length == 0)
            {
                return Array.Empty<
                    EventActionTemplate>();
            }

            var result =
                new EventActionTemplate[
                    source.Length];

            for (var i = 0;
                 i < source.Length;
                 i++)
            {
                var value =
                    source[i];

                result[i] =
                    value == null
                        ? null
                        : new EventActionTemplate
                        {
                            ActionType =
                                value.ActionType,
                            TargetId =
                                value.TargetId,
                            Name = value.Name,
                            HasValue =
                                value.HasValue,
                            NumericSource =
                                value.NumericSource,
                            ConstantNumber =
                                value.ConstantNumber,
                            ConstantNumberY =
                                value.ConstantNumberY,
                            ConstantNumberZ =
                                value.ConstantNumberZ,
                            ConstantNumberW =
                                value.ConstantNumberW,
                            NumericScale =
                                value.NumericScale,
                            NumericOffset =
                                value.NumericOffset,
                            TextSource =
                                value.TextSource,
                            ConstantText =
                                value.ConstantText,
                            TextTransforms =
                                value.TextTransforms,
                            TextPrefix =
                                value.TextPrefix,
                            TextSuffix =
                                value.TextSuffix
                        };
            }

            return result;
        }
    }
}
