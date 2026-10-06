using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Broadcast.Soop;
using VCR.Runtime.Broadcast.SoopUnity;
using VCR.Runtime.Core;
using VCR.Runtime.Events;
using VCR.Runtime.Events.Unity;
using VCR.Runtime.Protocols.Osc;
using VCR.Runtime.Protocols.OscEvents;
using VCR.Runtime.Protocols.OscEventsUnity;
using VCR.Runtime.Protocols.WebSocket;
using VCR.Runtime.Protocols.WebSocketUnity;
using VCR.Runtime.Tracking;

namespace VCR.Editor.P8
{
    public static class P8ProtocolEventAdapterValidation
    {
        [MenuItem("VCR/P8/Validate Protocol Event Adapters")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            ValidateIngress(
                failures);
            ValidateEventSubscriberIsolation(
                failures);
            ValidateEventHubBounds(
                failures);
            ValidateEventHubDisableIsolation(
                failures);
            ValidateTrackingPresenceRecovery(
                failures);
            ValidateWebSocketProtocol(
                failures);
            ValidateWebSocketTransport(
                failures);
            ValidateWebSocketStopIsolation(
                failures);
            ValidateOscStopIsolation(
                failures);
            ValidateOscMapping(
                failures);
            ValidateOscDirectPacketPath(
                failures);
            ValidateSoopMapping(
                failures);
            ValidateUnityAdapters(
                failures);
            ValidateJsonIngressScratchReset(
                failures);
            ValidateDestroyedUnityAdapterRecovery(
                failures);
            ValidateThrowingConsumerIsolation(
                failures);
            ValidateAdapterSinkFailureContracts(
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P8 protocol/event adapter validation: PASS " +
                    "(ingress guards, WebSocket injection, SOOP chat/donation mapping, duplicate suppression, diagnostics)");
                return true;
            }

            Debug.LogError(
                "VCR P8 protocol/event adapter validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void ValidateIngress(
            List<string> failures)
        {
            var valid =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastChatMessage,
                    "external.test",
                    10,
                    actorId:
                        "user-1",
                    text:
                        "hello",
                    actorName:
                        "Tester");

            Expect(
                NormalizedEventIngressValidator
                    .TryValidate(
                        valid,
                        allowTrackingEvents: false,
                        out var validError) &&
                string.IsNullOrEmpty(
                    validError),
                "valid external normalized event must pass ingress validation",
                failures);

            var forgedTracking =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .TrackingSubjectLost,
                    "external.test",
                    10);

            Expect(
                !NormalizedEventIngressValidator
                    .TryValidate(
                        forgedTracking,
                        allowTrackingEvents: false,
                        out var trackingError) &&
                !string.IsNullOrEmpty(
                    trackingError),
                "external ingress must reject forged tracking-derived events",
                failures);

            var invalidAmount =
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastDonation,
                    "external.test",
                    10,
                    amount:
                        double.NaN,
                    currency:
                        "TEST",
                    hasAmount:
                        true);

            Expect(
                !NormalizedEventIngressValidator
                    .TryValidate(
                        invalidAmount,
                        allowTrackingEvents: false,
                        out var amountError) &&
                !string.IsNullOrEmpty(
                    amountError),
                "external ingress must reject non-finite donation amounts",
                failures);
        }

        private static void ValidateEventSubscriberIsolation(
            List<string> failures)
        {
            var bus =
                new NormalizedEventBus();
            var delivered = 0;
            long deliveredSequence = 0;

            Action<NormalizedEvent> throwing =
                _ =>
                    throw new InvalidOperationException(
                        "synthetic subscriber failure");
            Action<NormalizedEvent> healthy =
                value =>
                {
                    delivered++;
                    deliveredSequence =
                        value.Sequence;
                };

            bus.Published +=
                throwing;
            bus.Published +=
                healthy;

            bus.Publish(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .LocalManual,
                    "subscriber.validation",
                    1));

            Expect(
                delivered == 1 &&
                deliveredSequence == 1 &&
                bus.Sequence == 1 &&
                bus.SubscriberFailureCount == 1 &&
                bus.LastSubscriberError != null &&
                bus.LastSubscriberError.Contains(
                    "synthetic",
                    StringComparison.OrdinalIgnoreCase),
                "normalized event bus must isolate a throwing subscriber and continue delivery to later subscribers with the same assigned sequence",
                failures);

            bus.Published -=
                throwing;

            bus.Publish(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .LocalManual,
                    "subscriber.validation",
                    2));

            Expect(
                delivered == 2 &&
                deliveredSequence == 2 &&
                bus.Sequence == 2 &&
                bus.SubscriberFailureCount == 1,
                "removing the throwing subscriber must preserve later delivery without incrementing the failure count",
                failures);
        }

        private static void ValidateEventHubBounds(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P8 Event Hub Bounds Validation");

                var hub =
                    root.AddComponent<
                        NormalizedEventHub>();

                SetPrivateField(
                    hub,
                    "maxQueuedEvents",
                    0);
                SetPrivateField(
                    hub,
                    "maxDispatchPerFrame",
                    0);

                for (var i = 0; i < 40; i++)
                {
                    hub.Publish(
                        new NormalizedEvent(
                            NormalizedEventTypes
                                .LocalManual,
                            "bounds.validation",
                            i,
                            sequence:
                                i));
                }

                Expect(
                    hub.MaxQueuedEvents == 32 &&
                    hub.MaxDispatchPerFrame == 1 &&
                    hub.QueuedCount == 32 &&
                    hub.DroppedCount == 8,
                    "event hub must enforce minimum queue/dispatch bounds at runtime even when serialized values are invalid",
                    failures);

                InvokeUpdate(
                    hub);

                Expect(
                    hub.DispatchedCount == 1 &&
                    hub.QueuedCount == 31,
                    "event hub minimum dispatch budget must remain one event per frame",
                    failures);

                SetPrivateField(
                    hub,
                    "maxQueuedEvents",
                    int.MaxValue);
                SetPrivateField(
                    hub,
                    "maxDispatchPerFrame",
                    int.MaxValue);

                Expect(
                    hub.MaxQueuedEvents == 8192 &&
                    hub.MaxDispatchPerFrame == 2048,
                    "event hub must enforce maximum queue/dispatch bounds at runtime",
                    failures);

                var webSocketInjection =
                    root.AddComponent<
                        WebSocketEventInjectionAdapter>();
                var webSocketTransport =
                    root.AddComponent<
                        WebSocketEventClientTransport>();
                var oscReceiver =
                    root.AddComponent<
                        OscNormalizedEventUdpReceiver>();
                var soop =
                    root.AddComponent<
                        SoopBridgeEventAdapter>();

                SetPrivateField(
                    webSocketInjection,
                    "maxMessageCharacters",
                    int.MaxValue);
                SetPrivateField(
                    webSocketTransport,
                    "maxMessageBytes",
                    int.MaxValue);
                SetPrivateField(
                    webSocketTransport,
                    "maxQueuedMessages",
                    int.MaxValue);
                SetPrivateField(
                    webSocketTransport,
                    "maxDispatchPerFrame",
                    int.MaxValue);
                SetPrivateField(
                    oscReceiver,
                    "localPort",
                    int.MaxValue);
                SetPrivateField(
                    oscReceiver,
                    "maxQueuedEvents",
                    int.MaxValue);
                SetPrivateField(
                    oscReceiver,
                    "maxDispatchPerFrame",
                    int.MaxValue);
                SetPrivateField(
                    soop,
                    "maxMessageCharacters",
                    int.MaxValue);
                SetPrivateField(
                    soop,
                    "rememberedEventIds",
                    int.MaxValue);

                Expect(
                    webSocketInjection.MaxMessageCharacters ==
                        262144 &&
                    webSocketTransport.MaxMessageBytes ==
                        1048576 &&
                    webSocketTransport.MaxQueuedMessages ==
                        8192 &&
                    webSocketTransport.MaxDispatchPerFrame ==
                        2048 &&
                    oscReceiver.LocalPort ==
                        65535 &&
                    oscReceiver.MaxQueuedEvents ==
                        8192 &&
                    oscReceiver.MaxDispatchPerFrame ==
                        2048 &&
                    soop.MaxMessageCharacters ==
                        262144 &&
                    soop.RememberedEventIdLimit ==
                        8192,
                    "protocol ingress must enforce configured maximum memory/dispatch bounds even when serialized values exceed Inspector ranges",
                    failures);

                SetPrivateField(
                    webSocketInjection,
                    "maxMessageCharacters",
                    0);
                SetPrivateField(
                    webSocketTransport,
                    "maxMessageBytes",
                    0);
                SetPrivateField(
                    webSocketTransport,
                    "maxQueuedMessages",
                    0);
                SetPrivateField(
                    webSocketTransport,
                    "maxDispatchPerFrame",
                    0);
                SetPrivateField(
                    oscReceiver,
                    "localPort",
                    0);
                SetPrivateField(
                    oscReceiver,
                    "maxQueuedEvents",
                    0);
                SetPrivateField(
                    oscReceiver,
                    "maxDispatchPerFrame",
                    0);
                SetPrivateField(
                    soop,
                    "maxMessageCharacters",
                    0);
                SetPrivateField(
                    soop,
                    "rememberedEventIds",
                    0);

                Expect(
                    webSocketInjection.MaxMessageCharacters ==
                        256 &&
                    webSocketTransport.MaxMessageBytes ==
                        1024 &&
                    webSocketTransport.MaxQueuedMessages ==
                        32 &&
                    webSocketTransport.MaxDispatchPerFrame ==
                        1 &&
                    oscReceiver.LocalPort ==
                        1 &&
                    oscReceiver.MaxQueuedEvents ==
                        32 &&
                    oscReceiver.MaxDispatchPerFrame ==
                        1 &&
                    soop.MaxMessageCharacters ==
                        256 &&
                    soop.RememberedEventIdLimit ==
                        32,
                    "protocol ingress must also enforce minimum runtime bounds for corrupted serialized values",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "event hub bounds validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void ValidateEventHubDisableIsolation(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P8 Event Hub Disable Isolation");

                var hub =
                    root.AddComponent<
                        NormalizedEventHub>();
                var publishedCount = 0;

                hub.Published += _ =>
                {
                    publishedCount++;
                };

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "hub-disable",
                        1));

                Expect(
                    hub.QueuedCount == 1,
                    "event hub disable isolation must queue an event before disable",
                    failures);

                InvokeHubDisable(
                    hub);

                hub.Publish(
                    new NormalizedEvent(
                        NormalizedEventTypes
                            .LocalManual,
                        "hub-disable",
                        2));

                InvokeUpdate(
                    hub);

                Expect(
                    hub.QueuedCount == 0 &&
                    publishedCount == 0 &&
                    hub.DroppedCount >= 2,
                    "disabled event hub must discard its backlog and reject new ingress instead of replaying stale events after lifecycle stop",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "event hub disable isolation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void ValidateTrackingPresenceRecovery(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P8 Tracking Presence Recovery");

                var oldProvider =
                    root.AddComponent<
                        P8FakePresenceProvider>();
                var sink =
                    root.AddComponent<
                        P8FakeEventSink>();
                var adapter =
                    root.AddComponent<
                        TrackingPresenceEventAdapter>();

                oldProvider.Presence =
                    new TrackingPresenceSnapshot(
                        sequence: 42,
                        timestampUs: 42,
                        subjectState:
                            SubjectPresenceState.Lost,
                        faceSourceAvailable:
                            false,
                        bodyHandsSourceAvailable:
                            false,
                        fullBodySourceAvailable:
                            false,
                        faceSubjectEvidence:
                            false,
                        bodyHandsSubjectEvidence:
                            false,
                        fullBodySubjectEvidence:
                            false,
                        anySourceAvailable:
                            false,
                        subjectEvidence:
                            false,
                        events:
                            TrackingPresenceEvents
                                .SubjectLost);

                adapter.SetProvider(
                    oldProvider);
                adapter.SetSink(
                    sink);
                InvokeUpdate(
                    adapter);

                Expect(
                    sink.Events.Count == 1 &&
                    sink.Events[0].Type ==
                        NormalizedEventTypes
                            .TrackingSubjectLost,
                    "tracking presence adapter must publish the initial provider event",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    oldProvider);

                var replacementProvider =
                    root.AddComponent<
                        P8FakePresenceProvider>();
                replacementProvider.Presence =
                    new TrackingPresenceSnapshot(
                        sequence: 42,
                        timestampUs: 43,
                        subjectState:
                            SubjectPresenceState.Present,
                        faceSourceAvailable:
                            true,
                        bodyHandsSourceAvailable:
                            false,
                        fullBodySourceAvailable:
                            false,
                        faceSubjectEvidence:
                            true,
                        bodyHandsSubjectEvidence:
                            false,
                        fullBodySubjectEvidence:
                            false,
                        anySourceAvailable:
                            true,
                        subjectEvidence:
                            true,
                        events:
                            TrackingPresenceEvents
                                .SubjectRestored);

                InvokeUpdate(
                    adapter);

                Expect(
                    sink.Events.Count == 2 &&
                    sink.Events[1].Type ==
                        NormalizedEventTypes
                            .TrackingSubjectRestored,
                    "tracking presence adapter must reset sequence state when a destroyed provider is replaced, even when the replacement reuses the same sequence value",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    sink);

                var replacementSink =
                    root.AddComponent<
                        P8FakeEventSink>();
                replacementProvider.Presence =
                    new TrackingPresenceSnapshot(
                        sequence: 43,
                        timestampUs: 44,
                        subjectState:
                            SubjectPresenceState.Present,
                        faceSourceAvailable:
                            false,
                        bodyHandsSourceAvailable:
                            false,
                        fullBodySourceAvailable:
                            false,
                        faceSubjectEvidence:
                            true,
                        bodyHandsSubjectEvidence:
                            false,
                        fullBodySubjectEvidence:
                            false,
                        anySourceAvailable:
                            false,
                        subjectEvidence:
                            true,
                        events:
                            TrackingPresenceEvents
                                .TrackingSourceLost);

                InvokeUpdate(
                    adapter);

                Expect(
                    replacementSink.Events.Count == 1 &&
                    replacementSink.Events[0].Type ==
                        NormalizedEventTypes
                            .TrackingSourceLost,
                    "tracking presence adapter must discard a destroyed cached sink and auto-discover a live replacement",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "tracking presence recovery validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void ValidateWebSocketProtocol(
            List<string> failures)
        {
            var message =
                new WebSocketEventMessage
                {
                    version =
                        WebSocketEventProtocol
                            .CurrentVersion,
                    op =
                        WebSocketEventProtocol
                            .InjectOperation,
                    type =
                        NormalizedEventTypes
                            .BroadcastChatMessage,
                    actorId =
                        "ws-user",
                    actorName =
                        "WS User",
                    text =
                        "hello websocket"
                };

            var created =
                WebSocketEventProtocol
                    .TryCreateEvent(
                        message,
                        "websocket.validation",
                        1234,
                        out var value,
                        out var error);

            Expect(
                created &&
                string.IsNullOrEmpty(
                    error) &&
                value.SourceId ==
                    "websocket.validation" &&
                value.TimestampUs == 1234 &&
                value.ActorId ==
                    "ws-user" &&
                value.ActorName ==
                    "WS User" &&
                value.Sequence == 0,
                "WebSocket protocol must force gateway source/timestamp and preserve normalized payload",
                failures);

            message.type =
                NormalizedEventTypes
                    .TrackingSourceLost;

            Expect(
                !WebSocketEventProtocol
                    .TryCreateEvent(
                        message,
                        "websocket.validation",
                        1234,
                        out _,
                        out var forgedError) &&
                !string.IsNullOrEmpty(
                    forgedError),
                "WebSocket injection must reject tracking-derived event types",
                failures);

            message.type =
                NormalizedEventTypes
                    .BroadcastChatMessage;
            message.version = 999;

            Expect(
                !WebSocketEventProtocol
                    .TryCreateEvent(
                        message,
                        "websocket.validation",
                        1234,
                        out _,
                        out var versionError) &&
                !string.IsNullOrEmpty(
                    versionError),
                "WebSocket injection must reject unsupported protocol versions",
                failures);
        }

        private static void ValidateWebSocketTransport(
            List<string> failures)
        {
            Expect(
                WebSocketEventClientTransport
                    .TryValidateEndpoint(
                        "ws://127.0.0.1:39541/vcr/events",
                        out var loopbackUri,
                        out var loopbackError) &&
                loopbackUri != null &&
                string.IsNullOrEmpty(
                    loopbackError),
                "loopback ws endpoint must be allowed for local bridge operation",
                failures);

            Expect(
                !WebSocketEventClientTransport
                    .TryValidateEndpoint(
                        "ws://example.com/vcr/events",
                        out _,
                        out var remotePlainError) &&
                !string.IsNullOrEmpty(
                    remotePlainError),
                "remote plaintext ws endpoint must be rejected",
                failures);

            Expect(
                WebSocketEventClientTransport
                    .TryValidateEndpoint(
                        "wss://example.com/vcr/events",
                        out var secureRemoteUri,
                        out var secureRemoteError) &&
                secureRemoteUri != null &&
                string.IsNullOrEmpty(
                    secureRemoteError),
                "remote wss endpoint must be accepted",
                failures);

            Expect(
                !WebSocketEventClientTransport
                    .TryValidateEndpoint(
                        "wss://user:password@example.com/vcr/events",
                        out _,
                        out var credentialError) &&
                !string.IsNullOrEmpty(
                    credentialError),
                "WebSocket endpoint URI user-info credentials must be rejected",
                failures);
        }

        private static void ValidateWebSocketStopIsolation(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P8 WebSocket Stop Isolation");

                var handler =
                    root.AddComponent<
                        P8ThrowingWebSocketHandler>();
                var transport =
                    root.AddComponent<
                        WebSocketEventClientTransport>();

                transport.SetHandler(
                    handler);

                Expect(
                    transport.TryQueueText(
                        "stale-after-stop",
                        out var queueError) &&
                    transport.QueuedCount == 1,
                    "WebSocket stop isolation must queue a pending message before stop: " +
                    queueError,
                    failures);

                transport.StopTransport();

                InvokeUpdate(
                    transport);

                var metrics =
                    new List<RuntimeMetric>();
                transport.CollectMetrics(
                    metrics);

                Expect(
                    transport.QueuedCount == 0 &&
                    handler.InvocationCount == 0 &&
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.transport.dropped",
                        out var dropped) &&
                    dropped >= 1.0,
                    "stopping WebSocket transport must discard queued messages so stale events cannot execute after stop",
                    failures);

                using var stoppingCancellation =
                    new System.Threading
                        .CancellationTokenSource();
                var stoppingTaskSource =
                    new System.Threading.Tasks
                        .TaskCompletionSource<bool>(
                            System.Threading.Tasks
                                .TaskCreationOptions
                                .RunContinuationsAsynchronously);

                SetPrivateField(
                    transport,
                    "_cancellation",
                    stoppingCancellation);
                SetPrivateField(
                    transport,
                    "_runTask",
                    stoppingTaskSource.Task);

                transport.StopTransport();

                Expect(
                    stoppingCancellation
                        .IsCancellationRequested &&
                    ReferenceEquals(
                        GetPrivateField<
                            System.Threading.Tasks.Task>(
                                transport,
                                "_runTask"),
                        stoppingTaskSource.Task),
                    "WebSocket stop must retain ownership of an incomplete run task until it actually completes",
                    failures);

                Expect(
                    transport.StartTransport(
                        out var deferredRestartError) &&
                    string.IsNullOrEmpty(
                        deferredRestartError) &&
                    GetPrivateField<bool>(
                        transport,
                        "_pendingStartRequested") &&
                    ReferenceEquals(
                        GetPrivateField<
                            System.Threading.Tasks.Task>(
                                transport,
                                "_runTask"),
                        stoppingTaskSource.Task),
                    "WebSocket restart must be deferred instead of overlapping an earlier run that is still stopping",
                    failures);

                transport.StopTransport();

                Expect(
                    !GetPrivateField<bool>(
                        transport,
                        "_pendingStartRequested"),
                    "explicit WebSocket stop must cancel a deferred restart request",
                    failures);

                stoppingTaskSource.SetResult(
                    true);
                InvokeUpdate(
                    transport);

                Expect(
                    GetPrivateField<
                        System.Threading.Tasks.Task>(
                            transport,
                            "_runTask") == null &&
                    GetPrivateField<
                        System.Threading
                            .CancellationTokenSource>(
                                transport,
                                "_cancellation") == null,
                    "completed WebSocket stop must release run-task and cancellation ownership on the main-thread lifecycle pass",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "WebSocket stop isolation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void ValidateOscStopIsolation(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P8 OSC Stop Isolation");

                var sink =
                    root.AddComponent<
                        P8ThrowingEventSink>();
                var receiver =
                    root.AddComponent<
                        OscNormalizedEventUdpReceiver>();

                receiver.SetSink(
                    sink);

                var message =
                    new OscMessage(
                        OscNormalizedEventMapper
                            .EventAddress,
                        new[]
                        {
                            OscArgument.FromString(
                                NormalizedEventTypes
                                    .LocalManual),
                            OscArgument.FromString(
                                "stop-isolation"),
                            OscArgument.FromString(
                                "stale-after-stop")
                        });

                Expect(
                    receiver.TryQueueMessage(
                        message,
                        out var queueError) &&
                    receiver.QueuedCount == 1,
                    "OSC stop isolation must queue a pending event before stop: " +
                    queueError,
                    failures);

                Expect(
                    InvokeStopReceiver(
                        receiver),
                    "OSC stop isolation must stop cleanly without a live UDP worker",
                    failures);

                InvokeUpdate(
                    receiver);

                var metrics =
                    new List<RuntimeMetric>();
                receiver.CollectMetrics(
                    metrics);

                Expect(
                    receiver.QueuedCount == 0 &&
                    sink.InvocationCount == 0 &&
                    TryGetMetric(
                        metrics,
                        "protocol.osc.events.dropped",
                        out var dropped) &&
                    dropped >= 1.0,
                    "stopping OSC event receiver must discard queued events so stale triggers cannot execute after disable/restart",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "OSC stop isolation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static void ValidateOscMapping(
            List<string> failures)
        {
            var chat =
                new OscMessage(
                    OscNormalizedEventMapper
                        .EventAddress,
                    new[]
                    {
                        OscArgument.FromString(
                            NormalizedEventTypes
                                .BroadcastChatMessage),
                        OscArgument.FromString(
                            "osc-user"),
                        OscArgument.FromString(
                            "hello osc")
                    });

            Expect(
                OscNormalizedEventMapper
                    .TryCreateEvent(
                        chat,
                        "osc.validation",
                        1500,
                        out var chatEvent,
                        out var chatError) &&
                string.IsNullOrEmpty(
                    chatError) &&
                chatEvent.Type ==
                    NormalizedEventTypes
                        .BroadcastChatMessage &&
                chatEvent.SourceId ==
                    "osc.validation" &&
                chatEvent.TimestampUs ==
                    1500 &&
                chatEvent.ActorId ==
                    "osc-user" &&
                chatEvent.Text ==
                    "hello osc",
                "OSC /vcr/event chat payload must map to normalized event using local source/timestamp",
                failures);

            var donation =
                new OscMessage(
                    OscNormalizedEventMapper
                        .EventAddress,
                    new[]
                    {
                        OscArgument.FromString(
                            NormalizedEventTypes
                                .BroadcastDonation),
                        OscArgument.FromString(
                            "osc-supporter"),
                        OscArgument.FromString(
                            "cheer"),
                        OscArgument.FromInt(
                            25),
                        OscArgument.FromString(
                            "TEST_UNIT"),
                        OscArgument.FromString(
                            "OSC Supporter")
                    });

            Expect(
                OscNormalizedEventMapper
                    .TryCreateEvent(
                        donation,
                        "osc.validation",
                        1501,
                        out var donationEvent,
                        out var donationError) &&
                string.IsNullOrEmpty(
                    donationError) &&
                donationEvent.HasAmount &&
                Math.Abs(
                    donationEvent.Amount -
                    25.0) <
                    0.001 &&
                donationEvent.Currency ==
                    "TEST_UNIT" &&
                donationEvent.ActorName ==
                    "OSC Supporter",
                "OSC /vcr/event donation payload must support numeric amount, unit, and actor name",
                failures);

            var forged =
                new OscMessage(
                    OscNormalizedEventMapper
                        .EventAddress,
                    new[]
                    {
                        OscArgument.FromString(
                            NormalizedEventTypes
                                .TrackingSourceLost)
                    });

            Expect(
                !OscNormalizedEventMapper
                    .TryCreateEvent(
                        forged,
                        "osc.validation",
                        1502,
                        out _,
                        out var forgedError) &&
                !string.IsNullOrEmpty(
                    forgedError),
                "OSC normalized-event ingress must reject forged tracking-derived events",
                failures);

            var wrongAddress =
                new OscMessage(
                    "/other/event",
                    new[]
                    {
                        OscArgument.FromString(
                            NormalizedEventTypes
                                .LocalManual)
                    });

            Expect(
                !OscNormalizedEventMapper
                    .TryCreateEvent(
                        wrongAddress,
                        "osc.validation",
                        1503,
                        out _,
                        out var addressError) &&
                !string.IsNullOrEmpty(
                    addressError),
                "OSC event mapper must reject unrelated OSC addresses",
                failures);
        }

        private static void ValidateOscDirectPacketPath(
            List<string> failures)
        {
            var chat =
                OscPacketWriter.WriteMessage(
                    OscNormalizedEventMapper.EventAddress,
                    OscArgument.FromString(
                        NormalizedEventTypes.BroadcastChatMessage),
                    OscArgument.FromString(
                        "viewer-1"),
                    OscArgument.FromString(
                        "hello"));

            var unrelated =
                OscPacketWriter.WriteMessage(
                    "/not-vcr-event",
                    OscArgument.FromString(
                        "ignored"));

            var forged =
                OscPacketWriter.WriteMessage(
                    OscNormalizedEventMapper.EventAddress,
                    OscArgument.FromString(
                        NormalizedEventTypes.TrackingSourceLost));

            var donation =
                OscPacketWriter.WriteMessage(
                    OscNormalizedEventMapper.EventAddress,
                    OscArgument.FromString(
                        NormalizedEventTypes.BroadcastDonation),
                    OscArgument.FromString(
                        "viewer-2"),
                    OscArgument.FromString(
                        "support"),
                    OscArgument.FromInt(
                        25),
                    OscArgument.FromString(
                        "TEST_UNIT"),
                    OscArgument.FromString(
                        "Supporter"));

            var packet =
                OscPacketWriter.WriteBundle(
                    new[]
                    {
                        chat,
                        unrelated,
                        forged,
                        donation
                    });
            var events =
                new List<NormalizedEvent>();

            Expect(
                InvokeDirectOscPacketReader(
                    packet,
                    packet.Length,
                    "osc.direct.validation",
                    1600,
                    events,
                    out var rejected) &&
                rejected == 2 &&
                events.Count == 2 &&
                events[0].Type ==
                    NormalizedEventTypes.BroadcastChatMessage &&
                events[0].ActorId ==
                    "viewer-1" &&
                events[0].Text ==
                    "hello" &&
                events[1].Type ==
                    NormalizedEventTypes.BroadcastDonation &&
                events[1].ActorId ==
                    "viewer-2" &&
                events[1].HasAmount &&
                Math.Abs(
                    events[1].Amount -
                    25.0) <
                    0.001 &&
                events[1].Currency ==
                    "TEST_UNIT" &&
                events[1].ActorName ==
                    "Supporter",
                "direct OSC event packet reader must preserve bundle order, map valid events, and count unrelated/forged messages as semantic rejects",
                failures);

            Expect(
                !InvokeDirectOscPacketReader(
                    packet,
                    packet.Length - 1,
                    "osc.direct.validation",
                    1601,
                    events,
                    out var malformedRejected) &&
                malformedRejected == 0 &&
                events.Count == 0,
                "direct OSC event packet reader must validate the complete packet before exposing any event from a malformed/truncated bundle",
                failures);
        }

        private static bool InvokeDirectOscPacketReader(
            byte[] packet,
            int length,
            string sourceId,
            long timestampUs,
            List<NormalizedEvent> output,
            out int rejected)
        {
            var readerType =
                typeof(OscNormalizedEventMapper)
                    .Assembly
                    .GetType(
                        "VCR.Runtime.Protocols.OscEvents.OscNormalizedEventPacketReader");

            var method =
                readerType?.GetMethod(
                    "TryReadEvents",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    "OscNormalizedEventPacketReader",
                    "TryReadEvents");
            }

            var arguments =
                new object[]
                {
                    packet,
                    length,
                    sourceId,
                    timestampUs,
                    output,
                    0
                };

            var result =
                method.Invoke(
                    null,
                    arguments);

            rejected =
                arguments[5] is int value
                    ? value
                    : 0;

            return
                result is bool success &&
                success;
        }

        private static void ValidateSoopMapping(
            List<string> failures)
        {
            var chat =
                new SoopBridgeMessage
                {
                    version =
                        SoopBridgeEventMapper
                            .CurrentVersion,
                    type =
                        SoopBridgeEventMapper
                            .ChatType,
                    eventId =
                        "chat-1",
                    userId =
                        "soop-user",
                    nickname =
                        "SOOP User",
                    text =
                        "hello"
                };

            Expect(
                SoopBridgeEventMapper
                    .TryCreateEvent(
                        chat,
                        2000,
                        out var chatEvent,
                        out var chatError) &&
                string.IsNullOrEmpty(
                    chatError) &&
                chatEvent.Type ==
                    NormalizedEventTypes
                        .BroadcastChatMessage &&
                chatEvent.SourceId ==
                    SoopBridgeEventMapper
                        .SourceId &&
                chatEvent.ActorId ==
                    "soop-user" &&
                chatEvent.ActorName ==
                    "SOOP User" &&
                chatEvent.Text ==
                    "hello",
                "SOOP chat bridge message must map to normalized chat event",
                failures);

            var oversizedEventId =
                new SoopBridgeMessage
                {
                    version =
                        SoopBridgeEventMapper
                            .CurrentVersion,
                    type =
                        SoopBridgeEventMapper
                            .ChatType,
                    eventId =
                        new string(
                            'x',
                            SoopBridgeEventMapper
                                .MaxEventIdLength +
                            1),
                    userId =
                        "viewer",
                    text =
                        "hello"
                };

            Expect(
                !SoopBridgeEventMapper
                    .TryCreateEvent(
                        oversizedEventId,
                        2001,
                        out _,
                        out var eventIdError) &&
                !string.IsNullOrWhiteSpace(
                    eventIdError),
                "SOOP bridge mapper must reject oversized event ids before they can enter dedupe state",
                failures);

            var donation =
                new SoopBridgeMessage
                {
                    version =
                        SoopBridgeEventMapper
                            .CurrentVersion,
                    type =
                        SoopBridgeEventMapper
                            .DonationType,
                    eventId =
                        "donation-1",
                    userId =
                        "supporter",
                    nickname =
                        "Supporter",
                    text =
                        "cheer",
                    count = 100
                };

            Expect(
                SoopBridgeEventMapper
                    .TryCreateEvent(
                        donation,
                        2001,
                        out var donationEvent,
                        out var donationError) &&
                string.IsNullOrEmpty(
                    donationError) &&
                donationEvent.Type ==
                    NormalizedEventTypes
                        .BroadcastDonation &&
                donationEvent.HasAmount &&
                Math.Abs(
                    donationEvent.Amount -
                    100.0) <
                    0.001 &&
                donationEvent.Currency ==
                    SoopBridgeEventMapper
                        .StarBalloonUnit,
                "SOOP donation bridge message must map count to normalized support amount/unit",
                failures);

            donation.count = 0;

            Expect(
                !SoopBridgeEventMapper
                    .TryCreateEvent(
                        donation,
                        2002,
                        out _,
                        out var invalidDonationError) &&
                !string.IsNullOrEmpty(
                    invalidDonationError),
                "SOOP donation mapping must reject zero/negative support count",
                failures);
        }

        private static void ValidateUnityAdapters(
            List<string> failures)
        {
            GameObject host = null;

            try
            {
                host =
                    new GameObject(
                        "P8 Protocol Adapter Validation");

                var sink =
                    host.AddComponent<
                        P8FakeEventSink>();
                var webSocket =
                    host.AddComponent<
                        WebSocketEventInjectionAdapter>();
                var soop =
                    host.AddComponent<
                        SoopBridgeEventAdapter>();
                var osc =
                    host.AddComponent<
                        OscNormalizedEventUdpReceiver>();
                var webSocketTransport =
                    host.AddComponent<
                        WebSocketEventClientTransport>();

                webSocket.SetSink(
                    sink);
                soop.SetSink(
                    sink);
                osc.SetSink(
                    sink);
                webSocketTransport.SetHandler(
                    webSocket);

                var webSocketJson =
                    JsonUtility.ToJson(
                        new WebSocketEventMessage
                        {
                            version =
                                WebSocketEventProtocol
                                    .CurrentVersion,
                            op =
                                WebSocketEventProtocol
                                    .InjectOperation,
                            type =
                                NormalizedEventTypes
                                    .LocalManual,
                            actorId =
                                "operator",
                            actorName =
                                "Operator",
                            text =
                                "trigger"
                        });

                Expect(
                    webSocket.TryHandleText(
                        webSocketJson,
                        out var wsError) &&
                    string.IsNullOrEmpty(
                        wsError) &&
                    sink.Events.Count == 1 &&
                    sink.Events[0].Type ==
                        NormalizedEventTypes
                            .LocalManual,
                    "Unity WebSocket adapter must parse and publish valid event injection",
                    failures);

                var forgedJson =
                    JsonUtility.ToJson(
                        new WebSocketEventMessage
                        {
                            version =
                                WebSocketEventProtocol
                                    .CurrentVersion,
                            op =
                                WebSocketEventProtocol
                                    .InjectOperation,
                            type =
                                NormalizedEventTypes
                                    .TrackingSubjectLost
                        });

                Expect(
                    !webSocket.TryHandleText(
                        forgedJson,
                        out var forgedError) &&
                    !string.IsNullOrEmpty(
                        forgedError) &&
                    sink.Events.Count == 1,
                    "Unity WebSocket adapter must not publish rejected tracking event injection",
                    failures);

                var chat =
                    new SoopBridgeMessage
                    {
                        version =
                            SoopBridgeEventMapper
                                .CurrentVersion,
                        type =
                            SoopBridgeEventMapper
                                .ChatType,
                        eventId =
                            "evt-chat-1",
                        userId =
                            "viewer",
                        nickname =
                            "Viewer",
                        text =
                            "hi"
                    };

                var chatJson =
                    JsonUtility.ToJson(
                        chat);

                Expect(
                    soop.TryHandleText(
                        chatJson,
                        out var chatError) &&
                    string.IsNullOrEmpty(
                        chatError) &&
                    sink.Events.Count == 2,
                    "SOOP Unity adapter must publish valid chat bridge message",
                    failures);

                Expect(
                    soop.TryHandleText(
                        chatJson,
                        out var duplicateError) &&
                    string.IsNullOrEmpty(
                        duplicateError) &&
                    sink.Events.Count == 2 &&
                    soop.DuplicateCount == 1,
                    "SOOP Unity adapter must idempotently suppress duplicate event ids",
                    failures);

                var donationJson =
                    JsonUtility.ToJson(
                        new SoopBridgeMessage
                        {
                            version =
                                SoopBridgeEventMapper
                                    .CurrentVersion,
                            type =
                                SoopBridgeEventMapper
                                    .DonationType,
                            eventId =
                                "evt-donation-1",
                            userId =
                                "supporter",
                            nickname =
                                "Supporter",
                            count = 50
                        });

                Expect(
                    soop.TryHandleText(
                        donationJson,
                        out var donationError) &&
                    string.IsNullOrEmpty(
                        donationError) &&
                    sink.Events.Count == 3 &&
                    sink.Events[2].HasAmount &&
                    Math.Abs(
                        sink.Events[2].Amount -
                        50.0) <
                        0.001,
                    "SOOP Unity adapter must publish valid donation bridge message",
                    failures);

                var badDonationJson =
                    JsonUtility.ToJson(
                        new SoopBridgeMessage
                        {
                            version =
                                SoopBridgeEventMapper
                                    .CurrentVersion,
                            type =
                                SoopBridgeEventMapper
                                    .DonationType,
                            eventId =
                                "evt-donation-bad",
                            count = 0
                        });

                Expect(
                    !soop.TryHandleText(
                        badDonationJson,
                        out var badDonationError) &&
                    !string.IsNullOrEmpty(
                        badDonationError) &&
                    sink.Events.Count == 3,
                    "SOOP Unity adapter must reject invalid donation bridge message without publishing",
                    failures);

                Expect(
                    webSocketTransport.TryQueueText(
                        webSocketJson,
                        out var transportQueueError) &&
                    string.IsNullOrEmpty(
                        transportQueueError) &&
                    webSocketTransport.QueuedCount == 1,
                    "WebSocket transport source-free path must queue a complete text message before main-thread delivery",
                    failures);

                InvokeUpdate(
                    webSocketTransport);

                Expect(
                    sink.Events.Count == 4 &&
                    sink.Events[3].Type ==
                        NormalizedEventTypes
                            .LocalManual &&
                    webSocketTransport.QueuedCount == 0,
                    "WebSocket transport must deliver queued text to its handler only from main-thread Update",
                    failures);

                var oscMessage =
                    new OscMessage(
                        OscNormalizedEventMapper
                            .EventAddress,
                        new[]
                        {
                            OscArgument.FromString(
                                NormalizedEventTypes
                                    .LocalManual),
                            OscArgument.FromString(
                                "osc-operator"),
                            OscArgument.FromString(
                                "osc trigger")
                        });

                Expect(
                    osc.TryQueueMessage(
                        oscMessage,
                        out var oscError) &&
                    string.IsNullOrEmpty(
                        oscError) &&
                    osc.QueuedCount == 1,
                    "OSC UDP receiver test path must queue a valid mapped event before main-thread dispatch",
                    failures);

                InvokeUpdate(
                    osc);

                Expect(
                    sink.Events.Count == 5 &&
                    sink.Events[4].Type ==
                        NormalizedEventTypes
                            .LocalManual &&
                    sink.Events[4].SourceId ==
                        "osc.udp" &&
                    osc.QueuedCount == 0 &&
                    osc.DispatchedEventCount == 1,
                    "OSC UDP receiver must publish queued events only through its main-thread Update path",
                    failures);

                Expect(
                    !osc.TryQueueMessage(
                        new OscMessage(
                            OscNormalizedEventMapper
                                .EventAddress,
                            new[]
                            {
                                OscArgument.FromString(
                                    NormalizedEventTypes
                                        .TrackingSubjectLost)
                            }),
                        out var oscForgedError) &&
                    !string.IsNullOrEmpty(
                        oscForgedError) &&
                    osc.RejectedEventCount == 1,
                    "OSC UDP receiver must reject forged tracking events before they enter the queue",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();

                webSocket.CollectMetrics(
                    metrics);
                webSocketTransport.CollectMetrics(
                    metrics);
                soop.CollectMetrics(
                    metrics);
                osc.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.events.accepted",
                        out var wsAccepted) &&
                    Math.Abs(
                        wsAccepted - 2.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.events.rejected",
                        out var wsRejected) &&
                    Math.Abs(
                        wsRejected - 1.0) <
                    0.001,
                    "WebSocket adapter diagnostics must expose accepted/rejected counts",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.transport.received",
                        out var transportReceived) &&
                    Math.Abs(
                        transportReceived - 1.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.transport.dispatched",
                        out var transportDispatched) &&
                    Math.Abs(
                        transportDispatched - 1.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.transport.dropped",
                        out var transportDropped) &&
                    transportDropped < 0.5,
                    "WebSocket transport diagnostics must expose queued/main-thread delivery counters",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "broadcast.soop.accepted",
                        out var soopAccepted) &&
                    Math.Abs(
                        soopAccepted - 2.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "broadcast.soop.duplicates",
                        out var duplicates) &&
                    Math.Abs(
                        duplicates - 1.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "broadcast.soop.rejected",
                        out var soopRejected) &&
                    Math.Abs(
                        soopRejected - 1.0) <
                    0.001,
                    "SOOP adapter diagnostics must expose accepted/duplicate/rejected counts",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "protocol.osc.events.accepted",
                        out var oscAccepted) &&
                    Math.Abs(
                        oscAccepted - 1.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "protocol.osc.events.rejected",
                        out var oscRejected) &&
                    Math.Abs(
                        oscRejected - 1.0) <
                    0.001 &&
                    TryGetMetric(
                        metrics,
                        "protocol.osc.events.dispatched",
                        out var oscDispatched) &&
                    Math.Abs(
                        oscDispatched - 1.0) <
                    0.001,
                    "OSC event receiver diagnostics must expose accepted/rejected/dispatched counts",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected P8 adapter validation exception: " +
                    exception);
            }
            finally
            {
                if (host != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            host);
                }
            }
        }

        private static void ValidateJsonIngressScratchReset(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P8 JSON Ingress Scratch Reset");

                var sink =
                    root.AddComponent<
                        P8FakeEventSink>();
                var webSocket =
                    root.AddComponent<
                        WebSocketEventInjectionAdapter>();
                var soop =
                    root.AddComponent<
                        SoopBridgeEventAdapter>();

                webSocket.SetSink(
                    sink);
                soop.SetSink(
                    sink);

                var fullWebSocketJson =
                    "{\"version\":1,\"op\":\"event.inject\",\"type\":\"local.manual\",\"actorId\":\"operator\",\"actorName\":\"Operator\",\"text\":\"first\",\"amount\":12.5,\"currency\":\"TEST_UNIT\",\"hasAmount\":true}";
                var sparseWebSocketJson =
                    "{\"version\":1,\"op\":\"event.inject\",\"type\":\"local.manual\"}";

                Expect(
                    webSocket.TryHandleText(
                        fullWebSocketJson,
                        out var fullWebSocketError) &&
                    webSocket.TryHandleText(
                        sparseWebSocketJson,
                        out var sparseWebSocketError) &&
                    string.IsNullOrEmpty(
                        fullWebSocketError) &&
                    string.IsNullOrEmpty(
                        sparseWebSocketError) &&
                    sink.Events.Count == 2 &&
                    sink.Events[0].ActorId ==
                        "operator" &&
                    sink.Events[0].ActorName ==
                        "Operator" &&
                    sink.Events[0].Text ==
                        "first" &&
                    sink.Events[0].HasAmount &&
                    Math.Abs(
                        sink.Events[0].Amount -
                        12.5) <
                        0.001 &&
                    sink.Events[1].ActorId == null &&
                    sink.Events[1].ActorName == null &&
                    sink.Events[1].Text == null &&
                    !sink.Events[1].HasAmount &&
                    Math.Abs(
                        sink.Events[1].Amount) <
                        0.001 &&
                    sink.Events[1].Currency == null,
                    "WebSocket JSON scratch reuse must reset every omitted optional field before FromJsonOverwrite: " +
                    fullWebSocketError +
                    " / " +
                    sparseWebSocketError,
                    failures);

                sink.Events.Clear();

                var fullSoopJson =
                    "{\"version\":1,\"type\":\"chat\",\"eventId\":\"scratch-soop-1\",\"userId\":\"viewer\",\"nickname\":\"Viewer\",\"text\":\"first\"}";
                var sparseSoopJson =
                    "{\"version\":1,\"type\":\"chat\",\"eventId\":\"scratch-soop-2\",\"text\":\"second\"}";

                Expect(
                    soop.TryHandleText(
                        fullSoopJson,
                        out var fullSoopError) &&
                    soop.TryHandleText(
                        sparseSoopJson,
                        out var sparseSoopError) &&
                    string.IsNullOrEmpty(
                        fullSoopError) &&
                    string.IsNullOrEmpty(
                        sparseSoopError) &&
                    sink.Events.Count == 2 &&
                    sink.Events[0].ActorId ==
                        "viewer" &&
                    sink.Events[0].ActorName ==
                        "Viewer" &&
                    sink.Events[0].Text ==
                        "first" &&
                    sink.Events[1].ActorId == null &&
                    sink.Events[1].ActorName == null &&
                    sink.Events[1].Text ==
                        "second",
                    "SOOP JSON scratch reuse must reset omitted user/nickname fields instead of leaking the previous message: " +
                    fullSoopError +
                    " / " +
                    sparseSoopError,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "JSON ingress scratch reset validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            root);
                }
            }
        }

        private static void ValidateDestroyedUnityAdapterRecovery(
            List<string> failures)
        {
            GameObject webSocketRoot = null;
            GameObject oscRoot = null;
            GameObject soopRoot = null;

            try
            {
                webSocketRoot =
                    new GameObject(
                        "P8 WebSocket Handler Recovery");

                var webSocketSink =
                    webSocketRoot.AddComponent<
                        P8FakeEventSink>();
                var oldHandler =
                    webSocketRoot.AddComponent<
                        WebSocketEventInjectionAdapter>();
                var transport =
                    webSocketRoot.AddComponent<
                        WebSocketEventClientTransport>();

                oldHandler.SetSink(
                    webSocketSink);
                transport.SetHandler(
                    oldHandler);

                UnityEngine.Object.DestroyImmediate(
                    oldHandler);

                var replacementHandler =
                    webSocketRoot.AddComponent<
                        WebSocketEventInjectionAdapter>();
                replacementHandler.SetSink(
                    webSocketSink);

                var message =
                    JsonUtility.ToJson(
                        new WebSocketEventMessage
                        {
                            version =
                                WebSocketEventProtocol
                                    .CurrentVersion,
                            op =
                                WebSocketEventProtocol
                                    .InjectOperation,
                            type =
                                NormalizedEventTypes
                                    .LocalManual,
                            text =
                                "recovered-websocket"
                        });

                Expect(
                    transport.TryQueueText(
                        message,
                        out var queueError),
                    "WebSocket recovery validation must queue a message: " +
                    queueError,
                    failures);

                InvokeUpdate(
                    transport);

                Expect(
                    webSocketSink.Events.Count == 1 &&
                    webSocketSink.Events[0].Text ==
                        "recovered-websocket",
                    "WebSocket transport must discard a destroyed cached handler and auto-discover its replacement",
                    failures);

                UnityEngine.Object.DestroyImmediate(
                    webSocketSink);

                var replacementWebSocketSink =
                    webSocketRoot.AddComponent<
                        P8FakeEventSink>();

                var directWebSocketJson =
                    JsonUtility.ToJson(
                        new WebSocketEventMessage
                        {
                            version =
                                WebSocketEventProtocol
                                    .CurrentVersion,
                            op =
                                WebSocketEventProtocol
                                    .InjectOperation,
                            type =
                                NormalizedEventTypes
                                    .LocalManual,
                            text =
                                "recovered-websocket-sink"
                        });

                Expect(
                    replacementHandler.TryHandleText(
                        directWebSocketJson,
                        out var directWebSocketError) &&
                    string.IsNullOrEmpty(
                        directWebSocketError) &&
                    replacementWebSocketSink.Events.Count == 1 &&
                    replacementWebSocketSink.Events[0].Text ==
                        "recovered-websocket-sink",
                    "WebSocket injection adapter must discard a destroyed cached sink and auto-discover its replacement: " +
                    directWebSocketError,
                    failures);

                oscRoot =
                    new GameObject(
                        "P8 OSC Sink Recovery");

                var oldSink =
                    oscRoot.AddComponent<
                        P8FakeEventSink>();
                var receiver =
                    oscRoot.AddComponent<
                        OscNormalizedEventUdpReceiver>();

                receiver.SetSink(
                    oldSink);

                UnityEngine.Object.DestroyImmediate(
                    oldSink);

                var replacementSink =
                    oscRoot.AddComponent<
                        P8FakeEventSink>();

                var oscMessage =
                    new OscMessage(
                        OscNormalizedEventMapper
                            .EventAddress,
                        new[]
                        {
                            OscArgument.FromString(
                                NormalizedEventTypes
                                    .LocalManual),
                            OscArgument.FromString(
                                "osc-recovery"),
                            OscArgument.FromString(
                                "recovered-osc")
                        });

                Expect(
                    receiver.TryQueueMessage(
                        oscMessage,
                        out var oscQueueError),
                    "OSC recovery validation must queue a message: " +
                    oscQueueError,
                    failures);

                InvokeUpdate(
                    receiver);

                Expect(
                    replacementSink.Events.Count == 1 &&
                    replacementSink.Events[0].Text ==
                        "recovered-osc",
                    "OSC receiver must discard a destroyed cached sink and auto-discover its replacement",
                    failures);

                soopRoot =
                    new GameObject(
                        "P8 SOOP Sink Recovery");

                var oldSoopSink =
                    soopRoot.AddComponent<
                        P8FakeEventSink>();
                var soop =
                    soopRoot.AddComponent<
                        SoopBridgeEventAdapter>();

                soop.SetSink(
                    oldSoopSink);

                UnityEngine.Object.DestroyImmediate(
                    oldSoopSink);

                var replacementSoopSink =
                    soopRoot.AddComponent<
                        P8FakeEventSink>();

                var soopJson =
                    JsonUtility.ToJson(
                        new SoopBridgeMessage
                        {
                            version =
                                SoopBridgeEventMapper
                                    .CurrentVersion,
                            type =
                                SoopBridgeEventMapper
                                    .ChatType,
                            eventId =
                                "soop-recovery",
                            userId =
                                "viewer",
                            nickname =
                                "Viewer",
                            text =
                                "recovered-soop"
                        });

                Expect(
                    soop.TryHandleText(
                        soopJson,
                        out var soopRecoveryError) &&
                    string.IsNullOrEmpty(
                        soopRecoveryError) &&
                    replacementSoopSink.Events.Count == 1 &&
                    replacementSoopSink.Events[0].Text ==
                        "recovered-soop",
                    "SOOP adapter must discard a destroyed cached sink and auto-discover its replacement: " +
                    soopRecoveryError,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed protocol adapter recovery unexpected exception: " +
                    exception);
            }
            finally
            {
                if (webSocketRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        webSocketRoot);
                }

                if (oscRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        oscRoot);
                }

                if (soopRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        soopRoot);
                }
            }
        }

        private static void ValidateAdapterSinkFailureContracts(
            List<string> failures)
        {
            GameObject webSocketRoot = null;
            GameObject soopRoot = null;

            try
            {
                webSocketRoot =
                    new GameObject(
                        "P8 WebSocket Sink Failure Contract");

                var throwingWebSocketSink =
                    webSocketRoot.AddComponent<
                        P8ThrowingEventSink>();
                var webSocket =
                    webSocketRoot.AddComponent<
                        WebSocketEventInjectionAdapter>();
                webSocket.SetSink(
                    throwingWebSocketSink);

                var webSocketJson =
                    JsonUtility.ToJson(
                        new WebSocketEventMessage
                        {
                            version =
                                WebSocketEventProtocol
                                    .CurrentVersion,
                            op =
                                WebSocketEventProtocol
                                    .InjectOperation,
                            type =
                                NormalizedEventTypes
                                    .LocalManual,
                            text =
                                "retry-websocket"
                        });

                Expect(
                    !webSocket.TryHandleText(
                        webSocketJson,
                        out var failedWebSocketError) &&
                    !string.IsNullOrWhiteSpace(
                        failedWebSocketError) &&
                    webSocket.AcceptedCount == 0 &&
                    webSocket.RejectedCount == 1,
                    "WebSocket injection adapter TryHandleText must contain sink exceptions and count them as rejected",
                    failures);

                var replacementWebSocketSink =
                    webSocketRoot.AddComponent<
                        P8FakeEventSink>();
                webSocket.SetSink(
                    replacementWebSocketSink);

                Expect(
                    webSocket.TryHandleText(
                        webSocketJson,
                        out var recoveredWebSocketError) &&
                    string.IsNullOrEmpty(
                        recoveredWebSocketError) &&
                    replacementWebSocketSink.Events.Count == 1 &&
                    webSocket.AcceptedCount == 1,
                    "WebSocket injection adapter must recover after a sink failure and accept the next retry: " +
                    recoveredWebSocketError,
                    failures);

                soopRoot =
                    new GameObject(
                        "P8 SOOP Sink Failure Contract");

                var throwingSoopSink =
                    soopRoot.AddComponent<
                        P8ThrowingEventSink>();
                var soop =
                    soopRoot.AddComponent<
                        SoopBridgeEventAdapter>();
                soop.SetSink(
                    throwingSoopSink);

                var soopJson =
                    JsonUtility.ToJson(
                        new SoopBridgeMessage
                        {
                            version =
                                SoopBridgeEventMapper
                                    .CurrentVersion,
                            type =
                                SoopBridgeEventMapper
                                    .ChatType,
                            eventId =
                                "retry-after-sink-failure",
                            userId =
                                "viewer",
                            nickname =
                                "Viewer",
                            text =
                                "retry-soop"
                        });

                Expect(
                    !soop.TryHandleText(
                        soopJson,
                        out var failedSoopError) &&
                    !string.IsNullOrWhiteSpace(
                        failedSoopError) &&
                    soop.AcceptedCount == 0 &&
                    soop.RejectedCount == 1 &&
                    soop.DuplicateCount == 0,
                    "SOOP adapter must not commit dedupe state when sink delivery fails",
                    failures);

                var replacementSoopSink =
                    soopRoot.AddComponent<
                        P8FakeEventSink>();
                soop.SetSink(
                    replacementSoopSink);

                Expect(
                    soop.TryHandleText(
                        soopJson,
                        out var recoveredSoopError) &&
                    string.IsNullOrEmpty(
                        recoveredSoopError) &&
                    replacementSoopSink.Events.Count == 1 &&
                    replacementSoopSink.Events[0].Text ==
                        "retry-soop" &&
                    soop.AcceptedCount == 1 &&
                    soop.DuplicateCount == 0,
                    "SOOP adapter must allow the same event id to retry after failed delivery and commit dedupe only after success: " +
                    recoveredSoopError,
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "adapter sink failure contract validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (webSocketRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        webSocketRoot);
                }

                if (soopRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        soopRoot);
                }
            }
        }

        private static void ValidateThrowingConsumerIsolation(
            List<string> failures)
        {
            GameObject webSocketRoot = null;
            GameObject oscRoot = null;

            try
            {
                webSocketRoot =
                    new GameObject(
                        "P8 Throwing WebSocket Handler");

                var throwingHandler =
                    webSocketRoot.AddComponent<
                        P8ThrowingWebSocketHandler>();
                var transport =
                    webSocketRoot.AddComponent<
                        WebSocketEventClientTransport>();

                transport.SetHandler(
                    throwingHandler);

                Expect(
                    transport.TryQueueText(
                        "one",
                        out var firstQueueError) &&
                    transport.TryQueueText(
                        "two",
                        out var secondQueueError),
                    "throwing WebSocket handler validation must queue both messages: " +
                    firstQueueError +
                    " / " +
                    secondQueueError,
                    failures);

                InvokeUpdate(
                    transport);

                var webSocketMetrics =
                    new List<RuntimeMetric>();
                transport.CollectMetrics(
                    webSocketMetrics);

                Expect(
                    throwingHandler.InvocationCount == 2 &&
                    transport.QueuedCount == 0 &&
                    TryGetMetric(
                        webSocketMetrics,
                        "protocol.websocket.transport.handler_rejected",
                        out var handlerRejected) &&
                    Math.Abs(
                        handlerRejected - 2.0) <
                        0.001 &&
                    TryGetMetric(
                        webSocketMetrics,
                        "protocol.websocket.transport.handler_exceptions",
                        out var handlerExceptions) &&
                    Math.Abs(
                        handlerExceptions - 2.0) <
                        0.001,
                    "WebSocket transport must isolate handler exceptions, continue draining the frame budget, and report them separately",
                    failures);

                oscRoot =
                    new GameObject(
                        "P8 Throwing OSC Sink");

                var throwingSink =
                    oscRoot.AddComponent<
                        P8ThrowingEventSink>();
                var receiver =
                    oscRoot.AddComponent<
                        OscNormalizedEventUdpReceiver>();

                receiver.SetSink(
                    throwingSink);

                var firstOsc =
                    new OscMessage(
                        OscNormalizedEventMapper
                            .EventAddress,
                        new[]
                        {
                            OscArgument.FromString(
                                NormalizedEventTypes
                                    .LocalManual),
                            OscArgument.FromString(
                                "osc-throw"),
                            OscArgument.FromString(
                                "one")
                        });
                var secondOsc =
                    new OscMessage(
                        OscNormalizedEventMapper
                            .EventAddress,
                        new[]
                        {
                            OscArgument.FromString(
                                NormalizedEventTypes
                                    .LocalManual),
                            OscArgument.FromString(
                                "osc-throw"),
                            OscArgument.FromString(
                                "two")
                        });

                Expect(
                    receiver.TryQueueMessage(
                        firstOsc,
                        out var firstOscError) &&
                    receiver.TryQueueMessage(
                        secondOsc,
                        out var secondOscError),
                    "throwing OSC sink validation must queue both events: " +
                    firstOscError +
                    " / " +
                    secondOscError,
                    failures);

                InvokeUpdate(
                    receiver);

                var oscMetrics =
                    new List<RuntimeMetric>();
                receiver.CollectMetrics(
                    oscMetrics);

                Expect(
                    throwingSink.InvocationCount == 2 &&
                    receiver.QueuedCount == 0 &&
                    receiver.DispatchedEventCount == 0 &&
                    TryGetMetric(
                        oscMetrics,
                        "protocol.osc.events.dispatch_failures",
                        out var dispatchFailures) &&
                    Math.Abs(
                        dispatchFailures - 2.0) <
                        0.001,
                    "OSC receiver must isolate sink exceptions, continue draining the frame budget, and report dispatch failures separately",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "throwing protocol consumer isolation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (webSocketRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        webSocketRoot);
                }

                if (oscRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        oscRoot);
                }
            }
        }

        private static void InvokeUpdate(
            TrackingPresenceEventAdapter adapter)
        {
            var method =
                typeof(TrackingPresenceEventAdapter)
                    .GetMethod(
                        "Update",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(TrackingPresenceEventAdapter)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                adapter,
                null);
        }

        private static void InvokeHubDisable(
            NormalizedEventHub hub)
        {
            var method =
                typeof(
                    NormalizedEventHub)
                    .GetMethod(
                        "OnDisable",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(
                        NormalizedEventHub)
                        .FullName,
                    "OnDisable");
            }

            method.Invoke(
                hub,
                null);
        }

        private static void InvokeUpdate(
            NormalizedEventHub hub)
        {
            var method =
                typeof(NormalizedEventHub)
                    .GetMethod(
                        "Update",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(NormalizedEventHub)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                hub,
                null);
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field =
                target.GetType()
                    .GetField(
                        fieldName,
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    target.GetType().FullName,
                    fieldName);
            }

            field.SetValue(
                target,
                value);
        }

        private static T GetPrivateField<T>(
            object target,
            string fieldName)
        {
            var field =
                target.GetType()
                    .GetField(
                        fieldName,
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    target.GetType().FullName,
                    fieldName);
            }

            return (T)field.GetValue(
                target);
        }

        private static void InvokeUpdate(
            WebSocketEventClientTransport transport)
        {
            var method =
                typeof(
                    WebSocketEventClientTransport)
                    .GetMethod(
                        "Update",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(
                        WebSocketEventClientTransport)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                transport,
                null);
        }

        private static bool InvokeStopReceiver(
            OscNormalizedEventUdpReceiver receiver)
        {
            var method =
                typeof(
                    OscNormalizedEventUdpReceiver)
                    .GetMethod(
                        "StopReceiver",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(
                        OscNormalizedEventUdpReceiver)
                        .FullName,
                    "StopReceiver");
            }

            return
                method.Invoke(
                    receiver,
                    null) is bool stopped &&
                stopped;
        }

        private static void InvokeUpdate(
            OscNormalizedEventUdpReceiver receiver)
        {
            var method =
                typeof(
                    OscNormalizedEventUdpReceiver)
                    .GetMethod(
                        "Update",
                        System.Reflection
                            .BindingFlags.Instance |
                        System.Reflection
                            .BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(
                        OscNormalizedEventUdpReceiver)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                receiver,
                null);
        }

        private static bool TryGetMetric(
            List<RuntimeMetric> metrics,
            string name,
            out double value)
        {
            foreach (var metric in
                     metrics)
            {
                if (string.Equals(
                        metric.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    value =
                        metric.Value;
                    return true;
                }
            }

            value = 0.0;
            return false;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(
                    message);
            }
        }
    }

    internal sealed class P8ThrowingWebSocketHandler :
        MonoBehaviour,
        IWebSocketTextMessageHandler
    {
        public int InvocationCount
        {
            get;
            private set;
        }

        public bool TryHandleText(
            string message,
            out string error)
        {
            InvocationCount++;
            error = null;

            throw new InvalidOperationException(
                "synthetic websocket handler failure");
        }
    }

    internal sealed class P8ThrowingEventSink :
        MonoBehaviour,
        INormalizedEventSink
    {
        public int InvocationCount
        {
            get;
            private set;
        }

        public void Publish(
            NormalizedEvent value)
        {
            InvocationCount++;

            throw new InvalidOperationException(
                "synthetic OSC sink failure");
        }
    }

    internal sealed class P8FakePresenceProvider :
        MonoBehaviour,
        ITrackingPresenceProvider
    {
        public TrackingPresenceSnapshot Presence
        {
            get;
            set;
        }
    }

    internal sealed class P8FakeEventSink :
        MonoBehaviour,
        INormalizedEventSink
    {
        public List<NormalizedEvent> Events
        {
            get;
        } = new();

        public void Publish(
            NormalizedEvent value)
        {
            Events.Add(
                value);
        }
    }
}
