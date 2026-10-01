using System.Threading;
using Mediapipe;
using Mediapipe.Tasks.Vision.HolisticLandmarker;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Thread-safe bridge for the Holistic body/hand task.
    ///
    /// Face/head output is intentionally ignored. ADR-0025 assigns face/head
    /// ownership to FaceLandmarker or ARKit so pose loss cannot erase face data.
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
            _ = image;

            var regions = TrackingRegion.None;

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

            var sequence = Interlocked.Increment(ref _sequence);

            _latest.Publish(new TrackingFrame(
                sequence,
                timestampMillisec * 1000L,
                regions,
                float.NaN,
                regions != TrackingRegion.None));
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
