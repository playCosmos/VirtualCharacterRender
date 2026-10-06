using System;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Presentation2D
{
    public enum Character2DParameterSourceKind
    {
        FaceCoefficient = 0,
        StandardExpression = 1,
        HeadPositionX = 2,
        HeadPositionY = 3,
        HeadPositionZ = 4,
        HeadPitchDegrees = 5,
        HeadYawDegrees = 6,
        HeadRollDegrees = 7
    }

    [Serializable]
    public sealed class Character2DParameterBinding
    {
        public string TargetParameterId;
        public Character2DParameterSourceKind SourceKind =
            Character2DParameterSourceKind.FaceCoefficient;
        public FaceCoefficient FaceCoefficient =
            FaceCoefficient.Neutral;
        public StandardExpression StandardExpression =
            StandardExpression.Neutral;
        public float InputMin = 0f;
        public float InputMax = 1f;
        public float OutputMin = 0f;
        public float OutputMax = 1f;
        public bool ClampInput = true;
        public bool UseDefaultWhenUnavailable = true;
        public float DefaultInputValue = 0f;
    }

    [CreateAssetMenu(
        menuName = "VCR/P13/2D Parameter Mapping Profile",
        fileName = "VCR 2D Parameter Mapping")]
    public sealed class Character2DParameterMappingProfile :
        ScriptableObject
    {
        [SerializeField] private string backendId;
        [SerializeField] private Character2DParameterBinding[] bindings =
            Array.Empty<Character2DParameterBinding>();

        [NonSerialized] private int _revision;

        public string BackendId =>
            backendId;
        public int Revision =>
            _revision;
        public Character2DParameterBinding[] Bindings =>
            bindings ??
            Array.Empty<Character2DParameterBinding>();

        public void Configure(
            string targetBackendId,
            params Character2DParameterBinding[] values)
        {
            backendId =
                targetBackendId;
            bindings =
                values ??
                Array.Empty<Character2DParameterBinding>();
            unchecked
            {
                _revision++;
            }
        }

        private void OnValidate()
        {
            unchecked
            {
                _revision++;
            }
        }
    }

    public readonly struct Character2DParameterValue
    {
        public Character2DParameterValue(
            string parameterId,
            float value)
        {
            ParameterId = parameterId;
            Value = value;
        }

        public string ParameterId { get; }
        public float Value { get; }
    }

    public static class Character2DParameterMapper
    {
        public static bool TryValidate(
            Character2DParameterMappingProfile profile,
            out string error)
        {
            error = null;

            if (profile == null)
            {
                error =
                    "2D parameter mapping profile is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    profile.BackendId))
            {
                error =
                    "2D parameter mapping profile requires a backend id.";
                return false;
            }

            var bindings =
                profile.Bindings;

            for (var i = 0;
                 i < bindings.Length;
                 i++)
            {
                var binding =
                    bindings[i];

                if (binding == null ||
                    string.IsNullOrWhiteSpace(
                        binding.TargetParameterId))
                {
                    error =
                        "Every 2D parameter mapping requires a non-empty target parameter id.";
                    return false;
                }

                for (var j = 0;
                     j < i;
                     j++)
                {
                    var previous =
                        bindings[j];

                    if (previous != null &&
                        string.Equals(
                            previous.TargetParameterId,
                            binding.TargetParameterId,
                            StringComparison.Ordinal))
                    {
                        error =
                            $"Duplicate 2D target parameter id '{binding.TargetParameterId}'.";
                        return false;
                    }
                }

                if (!IsFinite(
                        binding.InputMin) ||
                    !IsFinite(
                        binding.InputMax) ||
                    !IsFinite(
                        binding.OutputMin) ||
                    !IsFinite(
                        binding.OutputMax) ||
                    !IsFinite(
                        binding.DefaultInputValue))
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' contains a non-finite mapping value.";
                    return false;
                }

                if (Mathf.Abs(
                        binding.InputMax -
                        binding.InputMin) <
                    0.000001f)
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' input range cannot have zero width.";
                    return false;
                }

                if ((int)binding.SourceKind < 0 ||
                    (int)binding.SourceKind >
                        (int)Character2DParameterSourceKind
                            .HeadRollDegrees)
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' uses unsupported source kind '{binding.SourceKind}'.";
                    return false;
                }

                if (binding.SourceKind ==
                        Character2DParameterSourceKind
                            .FaceCoefficient &&
                    ((int)binding.FaceCoefficient < 0 ||
                     (int)binding.FaceCoefficient >=
                        (int)FaceCoefficient.Count))
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' uses an invalid face coefficient.";
                    return false;
                }

                if (binding.SourceKind ==
                        Character2DParameterSourceKind
                            .StandardExpression &&
                    ((int)binding.StandardExpression <
                         0 ||
                     (int)binding.StandardExpression >=
                         (int)StandardExpression.Count))
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' uses an invalid standard expression.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryEvaluate(
            Character2DParameterMappingProfile profile,
            string backendId,
            Character2DInputSnapshot snapshot,
            out Character2DParameterValue[] values,
            out string error)
        {
            values =
                Array.Empty<
                    Character2DParameterValue>();
            error = null;

            if (!TryValidate(
                    profile,
                    out error))
            {
                return false;
            }

            return TryEvaluateValidated(
                profile,
                backendId,
                snapshot,
                out values,
                out error);
        }

        internal static bool TryEvaluateValidated(
            Character2DParameterMappingProfile profile,
            string backendId,
            Character2DInputSnapshot snapshot,
            out Character2DParameterValue[] values,
            out string error)
        {
            values =
                Array.Empty<
                    Character2DParameterValue>();
            error = null;

            if (profile == null)
            {
                error =
                    "2D parameter mapping profile is required.";
                return false;
            }

            if (!string.Equals(
                    profile.BackendId,
                    backendId,
                    StringComparison.Ordinal))
            {
                error =
                    $"2D parameter profile backend '{profile.BackendId}' does not match active backend '{backendId ?? "<null>"}'.";
                return false;
            }

            var bindings =
                profile.Bindings;
            var valueCount = 0;

            for (var i = 0;
                 i < bindings.Length;
                 i++)
            {
                var binding =
                    bindings[i];

                if (TryReadSource(
                        binding,
                        snapshot,
                        out _) ||
                    binding
                        .UseDefaultWhenUnavailable)
                {
                    valueCount++;
                }
            }

            if (valueCount == 0)
            {
                values =
                    Array.Empty<
                        Character2DParameterValue>();
                return true;
            }

            var result =
                new Character2DParameterValue[
                    valueCount];
            var resultIndex = 0;

            for (var i = 0;
                 i < bindings.Length;
                 i++)
            {
                var binding =
                    bindings[i];

                if (!TryReadSource(
                        binding,
                        snapshot,
                        out var sourceValue))
                {
                    if (!binding
                        .UseDefaultWhenUnavailable)
                    {
                        continue;
                    }

                    sourceValue =
                        binding.DefaultInputValue;
                }

                var normalized =
                    (sourceValue -
                     binding.InputMin) /
                    (binding.InputMax -
                     binding.InputMin);

                if (binding.ClampInput)
                {
                    normalized =
                        Mathf.Clamp01(
                            normalized);
                }

                var output =
                    Mathf.LerpUnclamped(
                        binding.OutputMin,
                        binding.OutputMax,
                        normalized);

                if (!IsFinite(
                        output))
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' evaluated to a non-finite value.";
                    values =
                        Array.Empty<
                            Character2DParameterValue>();
                    return false;
                }

                result[resultIndex++] =
                    new Character2DParameterValue(
                        binding.TargetParameterId,
                        output);
            }

            values =
                result;
            return true;
        }

        internal static bool TryEvaluateValidatedInto(
            Character2DParameterMappingProfile profile,
            string backendId,
            Character2DInputSnapshot snapshot,
            Span<Character2DParameterValue> destination,
            out int valueCount,
            out string error)
        {
            valueCount = 0;
            error = null;

            if (profile == null)
            {
                error =
                    "2D parameter mapping profile is required.";
                return false;
            }

            if (!string.Equals(
                    profile.BackendId,
                    backendId,
                    StringComparison.Ordinal))
            {
                error =
                    $"2D parameter profile backend '{profile.BackendId}' does not match active backend '{backendId ?? "<null>"}'.";
                return false;
            }

            var bindings =
                profile.Bindings;

            if (destination.Length <
                bindings.Length)
            {
                error =
                    "2D parameter mapping destination is smaller than the profile binding count.";
                return false;
            }

            for (var i = 0;
                 i < bindings.Length;
                 i++)
            {
                var binding =
                    bindings[i];

                if (!TryReadSource(
                        binding,
                        snapshot,
                        out var sourceValue))
                {
                    if (!binding
                        .UseDefaultWhenUnavailable)
                    {
                        continue;
                    }

                    sourceValue =
                        binding.DefaultInputValue;
                }

                var normalized =
                    (sourceValue -
                     binding.InputMin) /
                    (binding.InputMax -
                     binding.InputMin);

                if (binding.ClampInput)
                {
                    normalized =
                        Mathf.Clamp01(
                            normalized);
                }

                var output =
                    Mathf.LerpUnclamped(
                        binding.OutputMin,
                        binding.OutputMax,
                        normalized);

                if (!IsFinite(
                        output))
                {
                    error =
                        $"2D parameter '{binding.TargetParameterId}' evaluated to a non-finite value.";
                    valueCount = 0;
                    return false;
                }

                destination[valueCount++] =
                    new Character2DParameterValue(
                        binding.TargetParameterId,
                        output);
            }

            return true;
        }

        private static bool TryReadSource(
            Character2DParameterBinding binding,
            Character2DInputSnapshot snapshot,
            out float value)
        {
            value = 0f;

            switch (binding.SourceKind)
            {
                case Character2DParameterSourceKind
                    .FaceCoefficient:
                    if (snapshot.Face?.Face ==
                        null)
                    {
                        return false;
                    }

                    value =
                        snapshot.Face.Face.Get(
                            binding.FaceCoefficient);
                    return IsFinite(
                        value);

                case Character2DParameterSourceKind
                    .StandardExpression:
                    if (snapshot.Expressions
                        ?.Expressions ==
                        null)
                    {
                        return false;
                    }

                    value =
                        snapshot.Expressions
                            .Expressions
                            .Get(
                                binding.StandardExpression);
                    return IsFinite(
                        value);

                case Character2DParameterSourceKind
                    .HeadPositionX:
                case Character2DParameterSourceKind
                    .HeadPositionY:
                case Character2DParameterSourceKind
                    .HeadPositionZ:
                    if (snapshot.Face?.Face ==
                        null)
                    {
                        return false;
                    }

                    var position =
                        snapshot.Face.Face
                            .HeadPosition;
                    value =
                        binding.SourceKind ==
                            Character2DParameterSourceKind
                                .HeadPositionX
                            ? position.X
                            : binding.SourceKind ==
                              Character2DParameterSourceKind
                                  .HeadPositionY
                                ? position.Y
                                : position.Z;
                    return IsFinite(
                        value);

                case Character2DParameterSourceKind
                    .HeadPitchDegrees:
                case Character2DParameterSourceKind
                    .HeadYawDegrees:
                case Character2DParameterSourceKind
                    .HeadRollDegrees:
                    if (snapshot.Face?.Face ==
                        null)
                    {
                        return false;
                    }

                    var source =
                        snapshot.Face.Face
                            .HeadRotation;
                    var rotation =
                        new Quaternion(
                            source.X,
                            source.Y,
                            source.Z,
                            source.W);
                    var euler =
                        rotation.eulerAngles;

                    value =
                        binding.SourceKind ==
                            Character2DParameterSourceKind
                                .HeadPitchDegrees
                            ? SignedDegrees(
                                euler.x)
                            : binding.SourceKind ==
                              Character2DParameterSourceKind
                                  .HeadYawDegrees
                                ? SignedDegrees(
                                    euler.y)
                                : SignedDegrees(
                                    euler.z);
                    return IsFinite(
                        value);

                default:
                    return false;
            }
        }

        private static float SignedDegrees(
            float value)
        {
            value %=
                360f;

            if (value > 180f)
            {
                value -=
                    360f;
            }
            else if (value < -180f)
            {
                value +=
                    360f;
            }

            return value;
        }

        private static bool IsFinite(
            float value) =>
                !float.IsNaN(
                    value) &&
                !float.IsInfinity(
                    value);
    }
}
