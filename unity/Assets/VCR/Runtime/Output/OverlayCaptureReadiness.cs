namespace VCR.Runtime.Output
{
    public enum OverlayCaptureReadinessFailure
    {
        None = 0,
        Unsupported = 1,
        Faulted = 2,
        NativeApplyPending = 3,
        NotActive = 4,
        TransparencyDisabled = 5,
        InvalidClientSize = 6
    }

    public readonly struct OverlayCaptureReadiness
    {
        public OverlayCaptureReadiness(
            bool ready,
            OverlayCaptureReadinessFailure failure,
            string message)
        {
            Ready = ready;
            Failure = failure;
            Message = message;
        }

        public bool Ready { get; }
        public OverlayCaptureReadinessFailure Failure { get; }
        public string Message { get; }
    }

    public static class OverlayCaptureReadinessEvaluator
    {
        public static OverlayCaptureReadiness Evaluate(
            OverlayOutputStatus status,
            OverlayOutputSettings settings)
        {
            if (!status.Supported)
            {
                return Fail(
                    OverlayCaptureReadinessFailure.Unsupported,
                    "Overlay output is unsupported on the current platform.");
            }

            if (status.Faulted)
            {
                return Fail(
                    OverlayCaptureReadinessFailure.Faulted,
                    string.IsNullOrWhiteSpace(
                        status.LastError)
                        ? "Overlay output is faulted."
                        : status.LastError);
            }

            if (status.Pending)
            {
                return Fail(
                    OverlayCaptureReadinessFailure.NativeApplyPending,
                    "Native overlay settings are still being applied.");
            }

            if (!status.Active)
            {
                return Fail(
                    OverlayCaptureReadinessFailure.NotActive,
                    "Overlay output is not active.");
            }

            if (!settings.Transparent)
            {
                return Fail(
                    OverlayCaptureReadinessFailure.TransparencyDisabled,
                    "Transparent alpha output is disabled.");
            }

            if (status.ClientWidth <= 0 ||
                status.ClientHeight <= 0)
            {
                return Fail(
                    OverlayCaptureReadinessFailure.InvalidClientSize,
                    "Overlay client size is invalid.");
            }

            return new OverlayCaptureReadiness(
                true,
                OverlayCaptureReadinessFailure.None,
                null);
        }

        private static OverlayCaptureReadiness Fail(
            OverlayCaptureReadinessFailure failure,
            string message)
        {
            return new OverlayCaptureReadiness(
                false,
                failure,
                message);
        }
    }
}
