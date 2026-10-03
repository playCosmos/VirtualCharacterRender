using System;
using System.Collections.Generic;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    internal sealed class P11ExternalMotionImportOptions
    {
        public float BvhPositionScale = 0.01f;
        public bool BvhMirrorX = true;
    }

    internal sealed class P11ExternalMotionAdapterContext
    {
        public string SourceFilePath;
        public string SourceAssetPath;
        public string DestinationAssetFolder;
        public P11ExternalMotionMarkerFile MarkerFile;
        public P11ExternalMotionImportOptions Options;
        public ICollection<string> CreatedAssetPaths;
    }

    internal sealed class P11ExternalMotionAdapterResult
    {
        public string AdapterId;
        public string SourceAssetPath;
        public BakedMotionCueAsset[] CueAssets =
            Array.Empty<BakedMotionCueAsset>();
        public string[] CueAssetPaths =
            Array.Empty<string>();
        public int ImportedMarkerCount;
    }

    internal interface IP11ExternalMotionAdapter
    {
        string AdapterId { get; }

        bool SupportsExtension(
            string extension);

        bool TryImport(
            P11ExternalMotionAdapterContext context,
            out P11ExternalMotionAdapterResult result,
            out string error);
    }

    internal static class P11ExternalMotionAdapterRegistry
    {
        private static readonly IP11ExternalMotionAdapter[]
            Adapters =
            {
                new P11BvhMotionAdapter()
            };

        public static bool TryResolve(
            string extension,
            out IP11ExternalMotionAdapter adapter)
        {
            foreach (var candidate in
                     Adapters)
            {
                if (candidate != null &&
                    candidate.SupportsExtension(
                        extension))
                {
                    adapter =
                        candidate;
                    return true;
                }
            }

            adapter = null;
            return false;
        }

        public static IReadOnlyList<
            IP11ExternalMotionAdapter>
            All =>
                Adapters;
    }
}
