using System;
using VCR.Runtime.Scene;

namespace VCR.Runtime.Application
{
    [Serializable]
    public sealed class RuntimeConfigurationEnvelope
    {
        public int Version;
        public SceneRuntimeConfiguration Scene;
    }
}
