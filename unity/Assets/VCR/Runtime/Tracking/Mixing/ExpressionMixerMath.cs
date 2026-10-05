using System;
using System.Buffers;

namespace VCR.Runtime.Tracking.Mixing
{
    public static class ExpressionMixerMath
    {
        private struct CustomBlendEntry
        {
            public string Name;
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
                    mode),
                SnapshotArrayOwnership.Transfer);
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

            var aCustom =
                a.Custom;
            var bCustom =
                b.Custom;

            for (var i = 0;
                 i < aCustom.Length;
                 i++)
            {
                var item =
                    aCustom[i];

                if (string.IsNullOrWhiteSpace(
                        item.Name) ||
                    !IsLastCustomOccurrence(
                        aCustom,
                        i))
                {
                    continue;
                }

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

            for (var i = 0;
                 i < bCustom.Length;
                 i++)
            {
                var item =
                    bCustom[i];

                if (string.IsNullOrWhiteSpace(
                        item.Name) ||
                    !IsLastCustomOccurrence(
                        bCustom,
                        i))
                {
                    continue;
                }

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

            var entries =
                ArrayPool<CustomBlendEntry>
                    .Shared
                    .Rent(capacity);
            var count = 0;

            try
            {
                AddCustom(
                    baseState,
                    entries,
                    ref count,
                    deadzone: 0f,
                    layer: false);
                AddCustom(
                    layerState,
                    entries,
                    ref count,
                    deadzone,
                    layer: true);

                if (count == 0)
                {
                    return Array.Empty<NamedExpressionValue>();
                }

                SortCustomEntries(
                    entries,
                    count);

                var result =
                    new NamedExpressionValue[count];

                for (var i = 0;
                     i < count;
                     i++)
                {
                    var entry =
                        entries[i];

                    result[i] =
                        new NamedExpressionValue(
                            entry.Name,
                            BlendValue(
                                entry.BaseValue,
                                entry.LayerValue,
                                weight,
                                mode));
                }

                return result;
            }
            finally
            {
                for (var i = 0;
                     i < count;
                     i++)
                {
                    entries[i] = default;
                }

                ArrayPool<CustomBlendEntry>
                    .Shared
                    .Return(entries);
            }
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

            var custom =
                state.Custom;

            for (var i =
                     custom.Length - 1;
                 i >= 0;
                 i--)
            {
                if (string.Equals(
                    custom[i].Name,
                    name,
                    StringComparison.Ordinal))
                {
                    return custom[i].Value;
                }
            }

            return 0f;
        }

        private static bool IsLastCustomOccurrence(
            ReadOnlySpan<NamedExpressionValue> custom,
            int index)
        {
            if (index < 0 ||
                index >= custom.Length)
            {
                return false;
            }

            var name =
                custom[index].Name;

            for (var i =
                     index + 1;
                 i < custom.Length;
                 i++)
            {
                if (string.Equals(
                        custom[i].Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddCustom(
            NormalizedExpressionState state,
            CustomBlendEntry[] output,
            ref int count,
            float deadzone,
            bool layer)
        {
            if (state == null)
            {
                return;
            }

            foreach (var item in state.Custom)
            {
                if (string.IsNullOrWhiteSpace(
                        item.Name))
                {
                    continue;
                }

                var index =
                    FindCustomEntry(
                        output,
                        count,
                        item.Name);

                if (index < 0)
                {
                    index = count++;
                    output[index] =
                        new CustomBlendEntry
                        {
                            Name = item.Name
                        };
                }

                var entry =
                    output[index];
                var value =
                    ApplyDeadzone(
                        item.Value,
                        deadzone);

                if (layer)
                {
                    entry.LayerValue =
                        value;
                }
                else
                {
                    entry.BaseValue =
                        value;
                }

                output[index] =
                    entry;
            }
        }

        private static int FindCustomEntry(
            CustomBlendEntry[] entries,
            int count,
            string name)
        {
            for (var i = 0;
                 i < count;
                 i++)
            {
                if (string.Equals(
                        entries[i].Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SortCustomEntries(
            CustomBlendEntry[] entries,
            int count)
        {
            for (var i = 1;
                 i < count;
                 i++)
            {
                var current =
                    entries[i];
                var cursor =
                    i - 1;

                while (cursor >= 0 &&
                       string.Compare(
                           entries[cursor].Name,
                           current.Name,
                           StringComparison.Ordinal) >
                       0)
                {
                    entries[cursor + 1] =
                        entries[cursor];
                    cursor--;
                }

                entries[cursor + 1] =
                    current;
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
