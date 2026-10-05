using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Events.Unity
{
    /// <summary>
    /// Main-thread event hub with a bounded ingress queue for network/device
    /// adapters. Producers never invoke scene actions directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NormalizedEventHub :
        MonoBehaviour,
        INormalizedEventSink,
        IRuntimeMetricsSource
    {
        [SerializeField, Range(32, 8192)]
        private int maxQueuedEvents = 1024;

        [SerializeField, Range(1, 2048)]
        private int maxDispatchPerFrame = 128;

        private readonly ConcurrentQueue<NormalizedEvent> _queue = new();
        private readonly NormalizedEventBus _bus = new();

        private int _queuedCount;
        private long _dispatchedCount;
        private long _droppedCount;

        public event Action<NormalizedEvent> Published
        {
            add => _bus.Published += value;
            remove => _bus.Published -= value;
        }

        public int QueuedCount =>
            Math.Max(
                0,
                Volatile.Read(ref _queuedCount));

        public int MaxQueuedEvents =>
            Math.Max(
                32,
                Math.Min(
                    8192,
                    maxQueuedEvents));

        public int MaxDispatchPerFrame =>
            Math.Max(
                1,
                Math.Min(
                    2048,
                    maxDispatchPerFrame));

        public long DispatchedCount =>
            Interlocked.Read(ref _dispatchedCount);

        public long DroppedCount =>
            Interlocked.Read(ref _droppedCount);

        public void Publish(NormalizedEvent value)
        {
            _queue.Enqueue(value);
            var count =
                Interlocked.Increment(
                    ref _queuedCount);

            while (count > MaxQueuedEvents &&
                   _queue.TryDequeue(out _))
            {
                Interlocked.Decrement(
                    ref _queuedCount);
                Interlocked.Increment(
                    ref _droppedCount);
                count--;
            }
        }

        private void Update()
        {
            var budget =
                MaxDispatchPerFrame;

            for (var i = 0;
                 i < budget &&
                 _queue.TryDequeue(out var value);
                 i++)
            {
                Interlocked.Decrement(
                    ref _queuedCount);
                _bus.Publish(value);
                Interlocked.Increment(
                    ref _dispatchedCount);
            }
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "events.queue",
                QueuedCount,
                "count"));

            output.Add(new RuntimeMetric(
                "events.queue_limit",
                MaxQueuedEvents,
                "count"));

            output.Add(new RuntimeMetric(
                "events.dispatch_limit",
                MaxDispatchPerFrame,
                "count"));

            output.Add(new RuntimeMetric(
                "events.dispatched",
                DispatchedCount,
                "count"));

            output.Add(new RuntimeMetric(
                "events.dropped",
                DroppedCount,
                "count"));
        }
    }
}
