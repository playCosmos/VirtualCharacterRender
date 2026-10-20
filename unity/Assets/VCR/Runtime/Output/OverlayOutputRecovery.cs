using System;

namespace VCR.Runtime.Output
{
    public static class OverlayOutputRecovery
    {
        public static bool TryRestart(
            IOverlayOutputAdapter adapter,
            out string error)
        {
            error = null;

            if (adapter == null)
            {
                error =
                    "No overlay output adapter is configured.";
                return false;
            }

            OverlayOutputStatus status;

            try
            {
                var settings =
                    adapter.Settings;

                adapter.Shutdown();
                adapter.Apply(settings);

                status =
                    adapter.Status;
            }
            catch (Exception exception)
            {
                error =
                    "Overlay output restart failed: " +
                    exception.Message;
                return false;
            }

            if (!status.Supported)
            {
                error =
                    string.IsNullOrWhiteSpace(
                        status.LastError)
                        ? "Overlay output is unsupported."
                        : status.LastError;
                return false;
            }

            if (status.Faulted)
            {
                error =
                    string.IsNullOrWhiteSpace(
                        status.LastError)
                        ? "Overlay output remains faulted after restart."
                        : status.LastError;
                return false;
            }

            return true;
        }
    }
}
