using System;
using System.Threading;

namespace VCR.Runtime.Events
{
    /// <summary>
    /// Synchronous event dispatcher with a subscription snapshot.
    ///
    /// Threaded adapters should publish through the Unity event hub or another
    /// queueing adapter rather than invoke application actions directly.
    /// Subscriber exceptions are isolated so one consumer cannot prevent later
    /// consumers from receiving the same normalized event.
    /// </summary>
    public sealed class NormalizedEventBus :
        INormalizedEventSink
    {
        private readonly object _subscriptionSync =
            new();

        private Action<NormalizedEvent>[] _subscribers =
            Array.Empty<Action<NormalizedEvent>>();

        private long _sequence;
        private long _subscriberFailureCount;
        private string _lastSubscriberError;

        public event Action<NormalizedEvent> Published
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                lock (_subscriptionSync)
                {
                    var current =
                        _subscribers;
                    var next =
                        new Action<NormalizedEvent>[
                            current.Length + 1];

                    Array.Copy(
                        current,
                        next,
                        current.Length);
                    next[current.Length] =
                        value;

                    Volatile.Write(
                        ref _subscribers,
                        next);
                }
            }
            remove
            {
                if (value == null)
                {
                    return;
                }

                lock (_subscriptionSync)
                {
                    var current =
                        _subscribers;
                    var index = -1;

                    for (var i =
                             current.Length - 1;
                         i >= 0;
                         i--)
                    {
                        if (Equals(
                                current[i],
                                value))
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index < 0)
                    {
                        return;
                    }

                    if (current.Length == 1)
                    {
                        Volatile.Write(
                            ref _subscribers,
                            Array.Empty<
                                Action<NormalizedEvent>>());
                        return;
                    }

                    var next =
                        new Action<NormalizedEvent>[
                            current.Length - 1];

                    if (index > 0)
                    {
                        Array.Copy(
                            current,
                            0,
                            next,
                            0,
                            index);
                    }

                    if (index <
                        current.Length - 1)
                    {
                        Array.Copy(
                            current,
                            index + 1,
                            next,
                            index,
                            current.Length -
                            index -
                            1);
                    }

                    Volatile.Write(
                        ref _subscribers,
                        next);
                }
            }
        }

        public long Sequence =>
            Interlocked.Read(ref _sequence);

        public long SubscriberFailureCount =>
            Interlocked.Read(
                ref _subscriberFailureCount);

        public string LastSubscriberError =>
            Volatile.Read(
                ref _lastSubscriberError);

        public void Publish(NormalizedEvent value)
        {
            var sequence =
                Interlocked.Increment(ref _sequence);
            var published =
                value.WithSequence(sequence);
            var subscribers =
                Volatile.Read(
                    ref _subscribers);

            foreach (var subscriber in
                     subscribers)
            {
                try
                {
                    subscriber(published);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(
                        ref _subscriberFailureCount);
                    Volatile.Write(
                        ref _lastSubscriberError,
                        exception.Message);
                }
            }
        }
    }
}
