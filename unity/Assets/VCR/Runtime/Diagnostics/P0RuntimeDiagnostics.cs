using System;
using UnityEngine;

namespace VCR.Runtime.Diagnostics
{
    /// <summary>
    /// Compatibility component for P0-generated scenes.
    /// New P1 scenes should use RuntimeDiagnostics directly.
    /// </summary>
    [Obsolete("Use RuntimeDiagnostics for P1 and later scenes.")]
    [DisallowMultipleComponent]
    public sealed class P0RuntimeDiagnostics : RuntimeDiagnostics
    {
    }
}
