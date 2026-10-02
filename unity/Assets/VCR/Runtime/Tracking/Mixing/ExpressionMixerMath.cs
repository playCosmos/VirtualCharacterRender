using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking.Mixing
{
    public static class ExpressionMixerMath
    {
        public static NormalizedExpressionState Blend(
            NormalizedExpressionState baseState,
            NormalizedExpressionState layerState,
            float weight,
            float deadzone,
            ExpressionBlendMode mode)
        {
            var clampedWeight = Clamp01(weight);
            var clampedDeadzone = Clamp01(deadzone);
            var standard = new float[(int)StandardExpression.Count];

            for (var i = 0; i < standard.Length; i++)
            {
                var expression = (StandardExpression)i;
                var baseValue = baseState?.Get(expression) ?? 0f;
                var layerValue = ApplyDeadzone(
                    layerState?.Get(expression) ?? 0f,
                    clampedDeadzone);

                standard[i] = BlendValue(
                    baseValue,
                    layerValue,
                    clampedWeight,
                    mode);
            }

            return new NormalizedExpressionState(
                standard,
                BlendCustom(
                    baseState,
                    layerState,
                    clampedWeight,
                    clampedDeadzone,
                    mode));
        }

        public static float BlendValue(
            float baseValue,
            float layerValue,
            float weight,
            ExpressionBlendMode mode)
        {
            var b = Clamp01(baseValue);
            var l = Clamp01(layerValue);
            var w = Clamp01(weight);

            return mode switch
            {
                ExpressionBlendMode.Additive =>
                    Clamp01(b + l * w),
                ExpressionBlendMode.Maximum =>
                    Math.Max(b, l * w),
                _ =>
                    Clamp01(b + (l - b) * w)
            };
        }

        public static float ApplyDeadzone(
            float value,
            float deadzone)
        {
            var v = Clamp01(value);
            var d = Clamp01(deadzone);

            if (v <= d || d >= 0.9999f)
            {
                return 0f;
            }

            return Clamp01((v - d) / (1f - d));
        }

        public static float SmoothAlpha(
            float smoothingRate,
            float deltaSeconds)
        {
            if (smoothingRate <= 0f)
            {
                return 1f;
            }

            if (deltaSeconds <= 0f)
            {
                return 0f;
            }

            return Clamp01(
                1f -
                (float)Math.Exp(
                    -smoothingRate *
                    deltaSeconds));
        }

        private static NamedExpressionValue[] BlendCustom(
            NormalizedExpressionState baseState,
            NormalizedExpressionState layerState,
            float weight,
            float deadzone,
            ExpressionBlendMode mode)
        {
            var baseValues =
                new Dictionary<string, float>(
                    StringComparer.Ordinal);
            var layerValues =
                new Dictionary<string, float>(
                    StringComparer.Ordinal);

            AddCustom(baseState, baseValues, 0f);
            AddCustom(layerState, layerValues, deadzone);

            if (baseValues.Count == 0 &&
                layerValues.Count == 0)
            {
                return Array.Empty<NamedExpressionValue>();
            }

            var names =
                new HashSet<string>(
                    baseValues.Keys,
                    StringComparer.Ordinal);
            names.UnionWith(layerValues.Keys);

            var ordered = new List<string>(names);
            ordered.Sort(StringComparer.Ordinal);

            var result =
                new NamedExpressionValue[ordered.Count];

            for (var i = 0; i < ordered.Count; i++)
            {
                var name = ordered[i];
                baseValues.TryGetValue(
                    name,
                    out var baseValue);
                layerValues.TryGetValue(
                    name,
                    out var layerValue);

                result[i] =
                    new NamedExpressionValue(
                        name,
                        BlendValue(
                            baseValue,
                            layerValue,
                            weight,
                            mode));
            }

            return result;
        }

        private static void AddCustom(
            NormalizedExpressionState state,
            Dictionary<string, float> output,
            float deadzone)
        {
            if (state == null)
            {
                return;
            }

            foreach (var item in state.Custom)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                output[item.Name] =
                    ApplyDeadzone(
                        item.Value,
                        deadzone);
            }
        }

        private static float Clamp01(float value)
        {
            return Math.Max(
                0f,
                Math.Min(
                    1f,
                    value));
        }
    }
}
