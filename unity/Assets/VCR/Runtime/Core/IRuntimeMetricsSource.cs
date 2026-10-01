using System.Collections.Generic;

namespace VCR.Runtime.Core
{
    /// <summary>
    /// Optional diagnostics source. Collection is expected at low frequency,
    /// not every render frame.
    /// </summary>
    public interface IRuntimeMetricsSource
    {
        void CollectMetrics(List<RuntimeMetric> output);
    }
}
