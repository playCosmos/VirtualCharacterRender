namespace VCR.Runtime.Tracking.Routing
{
    public readonly struct TrackingRouteStatus
    {
        public TrackingRouteStatus(
            string faceSourceId,
            string bodyHandsSourceId,
            string fullBodySourceId,
            string expressionSourceId,
            bool preferredFaceActive,
            bool fallbackFaceInferenceEnabled,
            double faceAgeMs,
            double bodyHandsAgeMs,
            double fullBodyAgeMs,
            double expressionAgeMs)
        {
            FaceSourceId = faceSourceId;
            BodyHandsSourceId = bodyHandsSourceId;
            FullBodySourceId = fullBodySourceId;
            ExpressionSourceId = expressionSourceId;
            PreferredFaceActive = preferredFaceActive;
            FallbackFaceInferenceEnabled =
                fallbackFaceInferenceEnabled;
            FaceAgeMs = faceAgeMs;
            BodyHandsAgeMs = bodyHandsAgeMs;
            FullBodyAgeMs = fullBodyAgeMs;
            ExpressionAgeMs = expressionAgeMs;
        }

        public string FaceSourceId { get; }
        public string BodyHandsSourceId { get; }
        public string FullBodySourceId { get; }
        public string ExpressionSourceId { get; }
        public bool PreferredFaceActive { get; }
        public bool FallbackFaceInferenceEnabled { get; }

        public double FaceAgeMs { get; }
        public double BodyHandsAgeMs { get; }
        public double FullBodyAgeMs { get; }
        public double ExpressionAgeMs { get; }
    }
}
