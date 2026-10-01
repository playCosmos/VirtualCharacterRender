using System;

namespace VCR.Runtime.Tracking
{
    public sealed class NormalizedExpressionState
    {
        private readonly float[] _standard;
        private readonly NamedExpressionValue[] _custom;

        public NormalizedExpressionState(
            float[] standard,
            NamedExpressionValue[] custom = null)
        {
            _standard = standard ??
                throw new ArgumentNullException(nameof(standard));

            if (_standard.Length != (int)StandardExpression.Count)
            {
                throw new ArgumentException(
                    "Standard expression array has an invalid length.",
                    nameof(standard));
            }

            _custom = custom ?? Array.Empty<NamedExpressionValue>();
        }

        public float Get(StandardExpression expression)
        {
            var index = (int)expression;
            if (index < 0 || index >= _standard.Length)
            {
                return 0f;
            }

            return _standard[index];
        }

        public ReadOnlySpan<NamedExpressionValue> Custom => _custom;
    }
}
