using System;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Defines how immutable tracking payloads acquire caller-provided arrays.
    ///
    /// Transfer is allocation-free but requires the caller to relinquish all
    /// mutable access after construction. Copy defensively clones the array and
    /// is intended for untrusted/external ownership boundaries.
    /// </summary>
    public enum SnapshotArrayOwnership
    {
        Transfer = 0,
        Copy = 1
    }

    internal static class SnapshotArrayOwnershipUtility
    {
        public static T[] Acquire<T>(
            T[] source,
            int expectedLength,
            SnapshotArrayOwnership ownership,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(
                    parameterName);
            }

            if (source.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"Expected {expectedLength} items, got {source.Length}.",
                    parameterName);
            }

            return ownership switch
            {
                SnapshotArrayOwnership.Transfer =>
                    source,
                SnapshotArrayOwnership.Copy =>
                    (T[])source.Clone(),
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(ownership),
                        ownership,
                        "Unknown snapshot array ownership mode.")
            };
        }

        public static T[] AcquireVariable<T>(
            T[] source,
            SnapshotArrayOwnership ownership,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(
                    parameterName);
            }

            return ownership switch
            {
                SnapshotArrayOwnership.Transfer =>
                    source,
                SnapshotArrayOwnership.Copy =>
                    (T[])source.Clone(),
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(ownership),
                        ownership,
                        "Unknown snapshot array ownership mode.")
            };
        }
    }
}
