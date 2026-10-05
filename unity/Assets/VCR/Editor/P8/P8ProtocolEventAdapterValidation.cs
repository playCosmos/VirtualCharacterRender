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
            ValidateEventHubBounds(
                failures);
            ValidateWebSocketProtocol(
                failures);
            ValidateWebSocketTransport(
                failures);
            ValidateOscMapping(
                failures);
            ValidateSoopMapping(
                failures);
            ValidateUnityAdapters(
                failures);
            ValidateDestroyedUnityAdapterRecovery(
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
