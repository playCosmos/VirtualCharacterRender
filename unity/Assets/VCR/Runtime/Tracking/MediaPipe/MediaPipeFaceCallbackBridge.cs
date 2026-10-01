using System.Threading;
using Mediapipe;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Thread-safe bridge for the independent webcam face task.
    /// Detailed landmark/blendshape normalization is added after P0 coordinate
    /// validation; this bridge currently establishes validity/timing semantics.
    /// </summary>
    public sealed class MediaPipeFaceCallbackBridge
    {
        private readonly LatestValueBuffer<TrackingFrame> _latest = new();
        private long _sequence;

        public void OnResult(
            in FaceLandmarkerResult result,
            Image image,
            long timestampMillisec)
        {
            _ = image;

            var hasFace =
                result.faceLandmarks != null &&
                result.faceLandmarks.Count > 0 &&
                result.faceLandmarks[0].landmarks != null &&
                result.faceLandmarks[0].landmarks.Count > 0;

            var regions = hasFace
                ? TrackingRegion.Face | TrackingRegion.Head
                : TrackingRegion.None;

            var sequence = Interlocked.Increment(ref _sequence);

            _latest.Publish(new TrackingFrame(
                sequence,
                timestampMillisec * 1000L,
                regions,
                float.NaN,
                hasFace));
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            frame = _latest.TakeLatest();
            return frame != null;
        }
    }
}
