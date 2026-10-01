using UnityEditor;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Editor.P0
{
    public static class P0PresenceValidation
    {
        [MenuItem("VCR/P0/Validate Presence Resolver")]
        public static void Validate()
        {
            const long lostGraceUs = 500_000;
            const long restoreStableUs = 150_000;
            const long sourceStaleUs = 1_000_000;

            var resolver = new TrackingPresenceResolver(
                lostGraceUs,
                restoreStableUs,
                sourceStaleUs);
            resolver.Reset(0);

            var present0 = Frame(
                sequence: 1,
                timestampUs: 0,
                subjectDetected: true);

            var initial = resolver.Update(
                0,
                present0,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var stable = resolver.Update(
                200_000,
                present0,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var absent = Frame(
                sequence: 2,
                timestampUs: 250_000,
                subjectDetected: false);

            var withinGrace = resolver.Update(
                700_000,
                absent,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var lost = resolver.Update(
                750_000,
                absent,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var restoredCandidate = Frame(
                sequence: 3,
                timestampUs: 800_000,
                subjectDetected: true);

            _ = resolver.Update(
                800_000,
                restoredCandidate,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var restoredStable = Frame(
                sequence: 4,
                timestampUs: 950_000,
                subjectDetected: true);

            var restored = resolver.Update(
                950_000,
                restoredStable,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var sourceLost = resolver.Update(
                2_100_001,
                restoredStable,
                faceConfigured: true,
                bodyHandsFrame: null,
                bodyHandsConfigured: false);

            var pass =
                initial.SubjectState == SubjectPresenceState.Unknown &&
                stable.SubjectState == SubjectPresenceState.Present &&
                withinGrace.SubjectState == SubjectPresenceState.Present &&
                lost.SubjectState == SubjectPresenceState.Lost &&
                lost.HasEvent(TrackingPresenceEvents.SubjectLost) &&
                restored.SubjectState == SubjectPresenceState.Present &&
                restored.HasEvent(TrackingPresenceEvents.SubjectRestored) &&
                sourceLost.SubjectState == SubjectPresenceState.Unknown &&
                sourceLost.HasEvent(
                    TrackingPresenceEvents.TrackingSourceLost) &&
                !sourceLost.HasEvent(
                    TrackingPresenceEvents.SubjectLost);

            if (pass)
            {
                Debug.Log("VCR P0 presence resolver: PASS");
            }
            else
            {
                Debug.LogError(
                    "VCR P0 presence resolver: FAIL. " +
                    $"initial={initial.SubjectState}, " +
                    $"stable={stable.SubjectState}, " +
                    $"withinGrace={withinGrace.SubjectState}, " +
                    $"lost={lost.SubjectState}/{lost.Events}, " +
                    $"restored={restored.SubjectState}/{restored.Events}, " +
                    $"sourceLost={sourceLost.SubjectState}/{sourceLost.Events}");
            }
        }

        private static TrackingFrame Frame(
            long sequence,
            long timestampUs,
            bool subjectDetected)
        {
            return new TrackingFrame(
                sequence,
                timestampUs,
                subjectDetected
                    ? TrackingRegion.Face | TrackingRegion.Head
                    : TrackingRegion.None,
                subjectDetected ? 1f : 0f,
                subjectDetected,
                sourceId: "presence-test");
        }
    }
}
