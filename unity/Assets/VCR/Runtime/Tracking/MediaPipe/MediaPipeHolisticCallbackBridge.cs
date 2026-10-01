using System.Threading;
using Mediapipe;
using Mediapipe.Tasks.Vision.HolisticLandmarker;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Thread-safe bridge from MediaPipe LIVE_STREAM callbacks to the runtime.
    ///
    /// The callback may run off the Unity main thread. It therefore converts
    /// only source-neutral validity metadata here and publishes the newest frame
    /// into a single-slot buffer. Unity objects are not touched.
    /// </summary>
    public sealed class MediaPipeHolisticCallbackBridge
    {
        private readonly LatestValueBuffer<TrackingFrame> _latest = new();
        private long _sequence;

        public void OnResult(
            in HolisticLandmarkerResult result,
            Image image,
            long timestampMillisec)
        {
            var regions = TrackingRegion.None;

            if (HasLandmarks(result.faceLandmarks))
            {
                regions |= TrackingRegion.Face | TrackingRegion.Head;
            }

            if (HasLandmarks(result.poseLandmarks))
            {
                regions |= TrackingRegion.UpperBody;
            }

            if (HasLandmarks(result.leftHandLandmarks))
            {
                regions |= TrackingRegion.LeftHand;
            }

            if (HasLandmarks(result.rightHandLandmarks))
            {
                regions |= TrackingRegion.RightHand;
            }

            var subjectDetected =
                (regions & (TrackingRegion.Face | TrackingRegion.Head | TrackingRegion.UpperBody)) != 0;

            var sequence = Interlocked.Increment(ref _sequence);

            _latest.Publish(new TrackingFrame(
                sequence,
                timestampMillisec * 1000L,
                regions,
                float.NaN,
                subjectDetected));
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            frame = _latest.TakeLatest();
            return frame != null;
        }

        private static bool HasLandmarks(
            Mediapipe.Tasks.Components.Containers.NormalizedLandmarks landmarks)
        {
            return landmarks.landmarks != null && landmarks.landmarks.Count > 0;
        }
    }
}
