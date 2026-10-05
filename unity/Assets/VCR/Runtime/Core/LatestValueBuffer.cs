using System.Threading;

namespace VCR.Runtime.Core
{
    /// <summary>
    /// Single-slot cross-thread buffer.
    ///
    /// Producers replace stale data instead of building a queue. This is
    /// intentional for real-time tracking where fresh data is more valuable
    /// than processing every camera frame.
    /// </summary>
    public sealed class LatestValueBuffer<T> where T : class
    {
        private T _latest;

        public void Publish(T value)
        {
            Interlocked.Exchange(ref _latest, value);
        }

        public T TakeLatest()
        {
            return Interlocked.Exchange(ref _latest, null);
        }

        public void Clear()
        {
            Interlocked.Exchange(
                ref _latest,
                null);
        }

        public bool HasValue => Volatile.Read(ref _latest) != null;
    }
}
