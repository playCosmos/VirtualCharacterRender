using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Events;

namespace VCR.Editor.P0
{
    public static class P0EventRuntimeMenu
    {
        [MenuItem("VCR/P0/Validate Normalized Event Runtime")]
        public static void Validate()
        {
            var bus = new NormalizedEventBus();
            var received =
                new List<NormalizedEvent>();

            bus.Published += received.Add;

            bus.Publish(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .TrackingSubjectLost,
                    "tracking.presence",
                    1000));

            bus.Publish(
                new NormalizedEvent(
                    NormalizedEventTypes
                        .BroadcastDonation,
                    "broadcast-test",
                    2000,
                    actorId: "viewer-1",
                    text: "support",
                    amount: 1000,
                    currency: "KRW",
                    hasAmount: true));

            var trackingNameCorrect =
                NormalizedEventTypes
                    .TrackingSubjectLost ==
                    "tracking.subject_lost" &&
                !NormalizedEventTypes
                    .TrackingSubjectLost
                    .Contains("afk");

            var pass =
                received.Count == 2 &&
                received[0].Sequence == 1 &&
                received[1].Sequence == 2 &&
                trackingNameCorrect &&
                received[0].Type ==
                    NormalizedEventTypes
                        .TrackingSubjectLost &&
                received[1].Type ==
                    NormalizedEventTypes
                        .BroadcastDonation &&
                received[1].HasAmount &&
                received[1].Amount == 1000 &&
                received[1].Currency == "KRW";

            if (pass)
            {
                Debug.Log(
                    "VCR P0 normalized event runtime: PASS - tracking disappearance is subject_lost (not AFK), sequence ordering and donation payload are preserved.");
            }
            else
            {
                Debug.LogError(
                    "VCR P0 normalized event runtime: FAIL.");
            }
        }
    }
}
