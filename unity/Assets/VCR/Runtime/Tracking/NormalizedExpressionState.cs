using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Immutable standard/custom expression snapshot.
    ///
    /// Transfer avoids duplicate hot-path copies when the producer created
    /// dedicated arrays. Copy is available at external ownership boundaries.
    /// </summary>
    public sealed class NormalizedExpressionState
    {
        private readonly float[] _standard;
        private readonly NamedExpressionValue[] _custom;

        public NormalizedExpressionState(
            float[] standard,
            NamedExpressionValue[] custom = null)
            : this(
                standard,
                custom,
                SnapshotArrayOwnership.Transfer)
        {
        }

        public NormalizedExpressionState(
            float[] standard,
            NamedExpressionValue[] custom,
            SnapshotArrayOwnership ownership)
        {
            _standard =
                SnapshotArrayOwnershipUtility.Acquire(
                    standard,
                    (int)StandardExpression.Count,
                    ownership,
                    nameof(standard));

            _custom =
                custom == null
                    ? Array.Empty<
                        NamedExpressionValue>()
                    : SnapshotArrayOwnershipUtility
                        .AcquireVariable(
                            custom,
                            ownership,
                            nameof(custom));
        }

        public float Get(
            StandardExpression expression)
        {
            var index =
                (int)expression;

            if (index < 0 ||
                index >=
                    _standard.Length)
            {
                return 0f;
            }

            return _standard[index];
        }

        public ReadOnlySpan<
            NamedExpressionValue> Custom =>
                _custom;
    }
}
