using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Fixed-capacity timestamp correlation for LIVE_STREAM submissions.
    ///
    /// Completed callbacks are removed immediately. Submission history uses a
    /// fixed ring so a stalled/missing callback cannot grow managed memory, and
    /// reaching capacity evicts only the oldest still-pending timestamp instead
    /// of dropping latency correlation for every in-flight submission.
    /// </summary>
    internal sealed class PendingSubmissionTracker
    {
        private readonly Dictionary<long, long>
            _submittedAtUs;
        private readonly long[] _submissionOrder;

        private int _cursor;
        private int _historyCount;
        private long _evictionCount;

        public PendingSubmissionTracker(
            int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity));
            }

            _submittedAtUs =
                new Dictionary<long, long>(
                    capacity);
            _submissionOrder =
                new long[capacity];
        }

        public int Capacity =>
            _submissionOrder.Length;

        public int Count =>
            _submittedAtUs.Count;

        public long EvictionCount =>
            _evictionCount;

        public void Record(
            long timestampMillisec,
            long submittedAtUs)
        {
            if (_submittedAtUs.ContainsKey(
                    timestampMillisec))
            {
                _submittedAtUs[
                    timestampMillisec] =
                        submittedAtUs;
                return;
            }

            if (_historyCount ==
                _submissionOrder.Length)
            {
                var oldest =
                    _submissionOrder[
                        _cursor];

                if (_submittedAtUs.Remove(
                        oldest))
                {
                    _evictionCount++;
                }
            }
            else
            {
                _historyCount++;
            }

            _submissionOrder[
                _cursor] =
                    timestampMillisec;
            _cursor =
                (_cursor + 1) %
                _submissionOrder.Length;

            _submittedAtUs.Add(
                timestampMillisec,
                submittedAtUs);
        }

        public bool TryComplete(
            long timestampMillisec,
            out long submittedAtUs)
        {
            if (_submittedAtUs.TryGetValue(
                    timestampMillisec,
                    out submittedAtUs))
            {
                _submittedAtUs.Remove(
                    timestampMillisec);
                return true;
            }

            submittedAtUs = 0;
            return false;
        }

        public void Forget(
            long timestampMillisec)
        {
            _submittedAtUs.Remove(
                timestampMillisec);
        }

        public void Clear()
        {
            _submittedAtUs.Clear();
            _cursor = 0;
            _historyCount = 0;
            Array.Clear(
                _submissionOrder,
                0,
                _submissionOrder.Length);
        }
    }
}
