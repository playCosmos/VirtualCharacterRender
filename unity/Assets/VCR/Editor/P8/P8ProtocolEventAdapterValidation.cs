using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Broadcast.Soop;
using VCR.Runtime.Broadcast.SoopUnity;
using VCR.Runtime.Core;
using VCR.Runtime.Events;
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
            ValidateWebSocketProtocol(
                failures);
            ValidateSoopMapping(
                failures);
            ValidateUnityAdapters(
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

                webSocket.SetSink(
                    sink);
                soop.SetSink(
                    sink);

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

                var metrics =
                    new List<RuntimeMetric>();

                webSocket.CollectMetrics(
                    metrics);
                soop.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "protocol.websocket.events.accepted",
                        out var wsAccepted) &&
                    Math.Abs(
                        wsAccepted - 1.0) <
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
