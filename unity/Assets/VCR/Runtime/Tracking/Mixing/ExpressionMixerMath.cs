using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking.Mixing
{
    public static class ExpressionMixerMath
    {
        private struct CustomBlendValues
        {
            public float BaseValue;
            public float LayerValue;
        }

        public static NormalizedExpressionState Blend(
            NormalizedExpressionState baseState,
            NormalizedExpressionState layerState,
            float weight,
            float deadzone,
            ExpressionBlendMode mode)
        {
            var clampedWeight = Clamp01(weight);
            var clampedDeadzone = Clamp01(deadzone);

            if (clampedWeight <= 0f &&
                baseState != null)
            {
                return baseState;
            }

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

        public static bool ApproximatelyEqual(
            NormalizedExpressionState a,
            NormalizedExpressionState b,
            float epsilon = 0.001f)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            var threshold =
                Math.Max(
                    0f,
                    epsilon);

            for (var i = 0;
                 i < (int)StandardExpression.Count;
                 i++)
            {
                var expression =
                    (StandardExpression)i;

                if (Math.Abs(
                        a.Get(expression) -
                        b.Get(expression)) >
                    threshold)
                {
                    return false;
                }
            }

            foreach (var item in a.Custom)
            {
                if (Math.Abs(
                        item.Value -
                        GetCustomValue(
                            b,
                            item.Name)) >
                    threshold)
                {
                    return false;
                }
            }

            foreach (var item in b.Custom)
            {
                if (Math.Abs(
                        item.Value -
                        GetCustomValue(
                            a,
                            item.Name)) >
                    threshold)
                {
                    return false;
                }
            }

            return true;
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
            var capacity =
                (baseState != null
                    ? baseState.Custom.Length
                    : 0) +
                (layerState != null
                    ? layerState.Custom.Length
                    : 0);

            if (capacity == 0)
            {
                return Array.Empty<NamedExpressionValue>();
            }

            var values =
                new Dictionary<string, CustomBlendValues>(
                    capacity,
                    StringComparer.Ordinal);

            AddCustom(
                baseState,
                values,
                deadzone: 0f,
                layer: false);
            AddCustom(
                layerState,
                values,
                deadzone,
                layer: true);

            if (values.Count == 0)
            {
                return Array.Empty<NamedExpressionValue>();
            }

            var ordered =
                new List<string>(
                    values.Keys);
            ordered.Sort(
                StringComparer.Ordinal);

            var result =
                new NamedExpressionValue[
                    ordered.Count];

            for (var i = 0;
                 i < ordered.Count;
                 i++)
            {
                var name =
                    ordered[i];
                var custom =
                    values[name];

                result[i] =
                    new NamedExpressionValue(
                        name,
                        BlendValue(
                            custom.BaseValue,
                            custom.LayerValue,
                            weight,
                            mode));
            }

            return result;
        }

        private static float GetCustomValue(
            NormalizedExpressionState state,
            string name)
        {
            if (state == null ||
                string.IsNullOrWhiteSpace(name))
            {
                return 0f;
            }

            foreach (var item in state.Custom)
            {
                if (string.Equals(
                    item.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    return item.Value;
                }
            }

            return 0f;
        }

        private static void AddCustom(
            NormalizedExpressionState state,
            Dictionary<string, CustomBlendValues> output,
            float deadzone,
            bool layer)
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

                output.TryGetValue(
                    item.Name,
                    out var values);

                var value =
                    ApplyDeadzone(
                        item.Value,
                        deadzone);

                if (layer)
                {
                    values.LayerValue =
                        value;
                }
                else
                {
                    values.BaseValue =
                        value;
                }

                output[item.Name] =
                    values;
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
