namespace VCR.Runtime.Output
{
    public enum BroadcastCaptureTargetTier
    {
        Custom = 0,
        Minimum720p60 = 1,
        Recommended1080p60 = 2
    }

    public readonly struct BroadcastCaptureTarget
    {
        public BroadcastCaptureTarget(
            BroadcastCaptureTargetTier tier,
            int width,
            int height,
            int framesPerSecond)
        {
            Tier = tier;
            Width = width;
            Height = height;
            FramesPerSecond = framesPerSecond;
        }

        public BroadcastCaptureTargetTier Tier { get; }
        public int Width { get; }
        public int Height { get; }
        public int FramesPerSecond { get; }

        public static BroadcastCaptureTarget Minimum720p60 =>
            new(
                BroadcastCaptureTargetTier.Minimum720p60,
                1280,
                720,
                60);

        public static BroadcastCaptureTarget Recommended1080p60 =>
            new(
                BroadcastCaptureTargetTier.Recommended1080p60,
                1920,
                1080,
                60);
    }

    public enum BroadcastCaptureReadinessFailure
    {
        None = 0,
        OverlayNotReady = 1,
        InvalidTarget = 2,
        ResolutionMismatch = 3,
        FrameRateBelowTarget = 4,
        RunInBackgroundDisabled = 5
    }

    public readonly struct BroadcastCaptureReadiness
    {
        public BroadcastCaptureReadiness(
            bool ready,
            BroadcastCaptureReadinessFailure failure,
            string message)
        {
            Ready = ready;
            Failure = failure;
            Message = message;
        }

        public bool Ready { get; }
        public BroadcastCaptureReadinessFailure Failure { get; }
        public string Message { get; }
    }

    public static class BroadcastCaptureReadinessEvaluator
    {
        public static BroadcastCaptureReadiness Evaluate(
            BroadcastCaptureTarget target,
            OverlayCaptureReadiness overlay,
            int requestedWidth,
            int requestedHeight,
            int targetFramesPerSecond,
            bool runInBackground)
        {
            if (!overlay.Ready)
            {
                return Fail(
                    BroadcastCaptureReadinessFailure.OverlayNotReady,
                    string.IsNullOrWhiteSpace(
                        overlay.Message)
                        ? "Overlay output is not capture-ready."
                        : overlay.Message);
            }

            if (target.Width <= 0 ||
                target.Height <= 0 ||
                target.FramesPerSecond <= 0)
            {
                return Fail(
                    BroadcastCaptureReadinessFailure.InvalidTarget,
                    "Broadcast capture target dimensions and frame rate must be positive.");
            }

            if (requestedWidth != target.Width ||
                requestedHeight != target.Height)
            {
                return Fail(
                    BroadcastCaptureReadinessFailure.ResolutionMismatch,
                    $"Requested render size {requestedWidth}x{requestedHeight} does not match broadcast target {target.Width}x{target.Height}.");
            }

            if (targetFramesPerSecond <
                target.FramesPerSecond)
            {
                return Fail(
                    BroadcastCaptureReadinessFailure.FrameRateBelowTarget,
                    $"Target frame rate {targetFramesPerSecond} is below the broadcast target {target.FramesPerSecond}.");
            }

            if (!runInBackground)
            {
                return Fail(
                    BroadcastCaptureReadinessFailure.RunInBackgroundDisabled,
                    "Run In Background must be enabled for broadcast overlay operation.");
            }

            return new BroadcastCaptureReadiness(
                true,
                BroadcastCaptureReadinessFailure.None,
                null);
        }

        private static BroadcastCaptureReadiness Fail(
            BroadcastCaptureReadinessFailure failure,
            string message)
        {
            return new BroadcastCaptureReadiness(
                false,
                failure,
                message);
        }
    }
}
