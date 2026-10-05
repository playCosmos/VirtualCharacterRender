using System;
using VCR.Runtime.Events;

namespace VCR.Runtime.EventRuntime
{
    [Serializable]
    public sealed class EventRuleFilter
    {
        public string Type;
        public string SourceId;
        public string ActorId;
        public string TextContains;
        public bool RequireAmount;
        public bool HasMinimumAmount;
        public double MinimumAmount;
        public bool HasMaximumAmount;
        public double MaximumAmount;

        public bool Matches(NormalizedEvent value)
        {
            if (!string.IsNullOrWhiteSpace(Type) &&
                !string.Equals(Type, value.Type, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SourceId) &&
                !string.Equals(SourceId, value.SourceId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ActorId) &&
                !string.Equals(ActorId, value.ActorId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(TextContains) &&
                (value.Text == null ||
                 value.Text.IndexOf(
                     TextContains,
                     StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            if (RequireAmount && !value.HasAmount)
            {
                return false;
            }

            if (HasMinimumAmount &&
                (!IsFinite(
                     MinimumAmount) ||
                 !value.HasAmount ||
                 value.Amount <
                 MinimumAmount))
            {
                return false;
            }

            if (HasMaximumAmount &&
                (!IsFinite(
                     MaximumAmount) ||
                 !value.HasAmount ||
                 value.Amount >
                 MaximumAmount))
            {
                return false;
            }

            return true;
        }

        private static bool IsFinite(
            double value)
        {
            return
                !double.IsNaN(value) &&
                !double.IsInfinity(value);
        }
    }
}
