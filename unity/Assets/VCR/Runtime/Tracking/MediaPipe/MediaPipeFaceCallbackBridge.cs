using System.Threading;
using Mediapipe;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Thread-safe bridge for the independent webcam face task.
    /// MediaPipe-owned callback buffers are converted immediately into immutable
    /// source-neutral payloads before the callback returns.
    /// </summary>
    public sealed class MediaPipeFaceCallbackBridge
    {
        private readonly LatestValueBuffer<TrackingFrame> _latest = new();
        private readonly string _sourceId;
        private long _sequence;

        public MediaPipeFaceCallbackBridge(string sourceId)
        {
            _sourceId = sourceId;
        }

        public void OnResult(
            in FaceLandmarkerResult result,
            Image image,
            long timestampMillisec)
        {
            _ = image;

            var hasFace = MediaPipeFaceNormalizer.TryConvert(
                in result,
                out var face);

            var regions = hasFace
                ? TrackingRegion.Face | TrackingRegion.Head
                : TrackingRegion.None;

            var sequence = Interlocked.Increment(ref _sequence);

            _latest.Publish(new TrackingFrame(
                sequence,
                timestampMillisec * 1000L,
                regions,
                hasFace ? 1f : 0f,
                hasFace,
                face: face,
                sourceId: _sourceId));
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            frame = _latest.TakeLatest();
            return frame != null;
        }
    }
}
