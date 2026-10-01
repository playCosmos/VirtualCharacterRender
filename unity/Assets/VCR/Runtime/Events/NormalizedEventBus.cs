using System;
using System.Threading;

namespace VCR.Runtime.Events
{
    /// <summary>
    /// Synchronous main-thread event dispatcher.
    ///
    /// Threaded adapters should publish through the Unity event hub or another
    /// queueing adapter rather than invoke application actions directly.
    /// </summary>
    public sealed class NormalizedEventBus :
        INormalizedEventSink
    {
        private long _sequence;

        public event Action<NormalizedEvent> Published;

        public long Sequence =>
            Interlocked.Read(ref _sequence);

        public void Publish(NormalizedEvent value)
        {
            var sequence =
                Interlocked.Increment(ref _sequence);

            Published?.Invoke(
                value.WithSequence(sequence));
        }
    }
}
