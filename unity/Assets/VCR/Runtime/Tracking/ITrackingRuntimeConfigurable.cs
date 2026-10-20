using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Optional UI/application-facing configuration surface for a tracking
    /// runtime. The UI remains source-agnostic and renders the returned
    /// string settings, while each source owns validation and restart policy.
    /// </summary>
    public interface ITrackingRuntimeConfigurable
    {
        string ConfigurationTitle { get; }

        /// <summary>
        /// True when adding this source should immediately open its settings.
        /// </summary>
        bool OpenConfigurationOnAdd { get; }

        IReadOnlyList<TrackingRuntimeSetting>
            GetConfigurationSettings();

        bool TryApplyConfiguration(
            IReadOnlyDictionary<string, string> values,
            out string error);
    }

    public readonly struct TrackingRuntimeSetting
    {
        public TrackingRuntimeSetting(
            string key,
            string label,
            string value,
            string placeholder = null,
            string helpText = null)
        {
            Key = key ?? string.Empty;
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            Placeholder = placeholder ?? string.Empty;
            HelpText = helpText ?? string.Empty;
        }

        public string Key { get; }
        public string Label { get; }
        public string Value { get; }
        public string Placeholder { get; }
        public string HelpText { get; }
    }
}
