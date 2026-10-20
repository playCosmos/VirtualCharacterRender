namespace VCR.Runtime.Tracking.MediaPipe
{
    public readonly struct MediaPipeWebcamStatus
    {
        public MediaPipeWebcamStatus(
            MediaPipeWebcamLifecycleState state,
            string deviceName,
            int width,
            int height,
            int requestedFps,
            bool faceEnabled,
            WebcamPreprocessingMode preprocessingMode,
            long faceSubmitted,
            long holisticSubmitted,
            long facePoolDrops,
            long holisticPoolDrops,
            long readbackErrors,
            string error)
        {
            State = state;
            DeviceName = deviceName;
            Width = width;
            Height = height;
            RequestedFps = requestedFps;
            FaceEnabled = faceEnabled;
            PreprocessingMode = preprocessingMode;
            FaceSubmitted = faceSubmitted;
            HolisticSubmitted = holisticSubmitted;
            FacePoolDrops = facePoolDrops;
            HolisticPoolDrops = holisticPoolDrops;
            ReadbackErrors = readbackErrors;
            Error = error;
        }

        public MediaPipeWebcamLifecycleState State { get; }
        public string DeviceName { get; }
        public int Width { get; }
        public int Height { get; }
        public int RequestedFps { get; }
        public bool FaceEnabled { get; }
        public WebcamPreprocessingMode PreprocessingMode { get; }
        public long FaceSubmitted { get; }
        public long HolisticSubmitted { get; }
        public long FacePoolDrops { get; }
        public long HolisticPoolDrops { get; }
        public long ReadbackErrors { get; }
        public string Error { get; }

        public bool IsRunning =>
            State == MediaPipeWebcamLifecycleState.Running;
    }
}
