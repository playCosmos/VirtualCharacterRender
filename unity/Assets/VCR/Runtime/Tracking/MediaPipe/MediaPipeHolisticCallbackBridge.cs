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

            var upperBody = MediaPipeHolisticNormalizer.ConvertUpperBody(in result);
            var leftHand = MediaPipeHolisticNormalizer.ConvertLeftHand(in result);
            var rightHand = MediaPipeHolisticNormalizer.ConvertRightHand(in result);

            var regions = TrackingRegion.None;
            if (upperBody != null) regions |= TrackingRegion.UpperBody;
            if (leftHand != null) regions |= TrackingRegion.LeftHand;
            if (rightHand != null) regions |= TrackingRegion.RightHand;

            var confidence =
                MediaPipeHolisticNormalizer.EstimateUpperBodyConfidence(upperBody);

            var sequence = Interlocked.Increment(ref _sequence);

            _latest.Publish(new TrackingFrame(
                sequence,
                timestampMillisec * 1000L,
                regions,
                confidence,
                regions != TrackingRegion.None,
                upperBody: upperBody,
                leftHand: leftHand,
                rightHand: rightHand));
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            frame = _latest.TakeLatest();
            return frame != null;
        }
    }
}
