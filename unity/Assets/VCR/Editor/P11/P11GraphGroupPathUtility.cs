using System;
using System.Collections.Generic;

namespace VCR.Editor.P11
{
    internal static class P11GraphGroupPathUtility
    {
        public static bool TryNormalize(
            string groupPath,
            out string normalized,
            out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(
                    groupPath))
            {
                error =
                    "Graph group path is required.";
                return false;
            }

            var rawSegments =
                groupPath
                    .Trim()
                    .Split(
                        new[]
                        {
                            '/'
                        },
                        StringSplitOptions.None);
            var segments =
                new List<string>(
                    rawSegments.Length);

            foreach (var raw in rawSegments)
            {
                var segment =
                    raw?.Trim();

                if (string.IsNullOrWhiteSpace(
                        segment))
                {
                    error =
                        "Graph group path cannot contain empty segments.";
                    return false;
                }

                if (segment == "." ||
                    segment == "..")
                {
                    error =
                        "Graph group path cannot use '.' or '..' segments.";
                    return false;
                }

                segments.Add(
                    segment);
            }

            normalized =
                string.Join(
                    "/",
                    segments);
            return true;
        }

        public static string[] CaptureHierarchyPaths(
            IEnumerable<string> groupPaths)
        {
            var result =
                new SortedSet<string>(
                    StringComparer.Ordinal);

            foreach (var raw in
                     groupPaths ??
                     Array.Empty<string>())
            {
                if (!TryNormalize(
                        raw,
                        out var normalized,
                        out _))
                {
                    continue;
                }

                var segments =
                    normalized.Split('/');

                for (var i = 1;
                     i <= segments.Length;
                     i++)
                {
                    result.Add(
                        string.Join(
                            "/",
                            segments,
                            0,
                            i));
                }
            }

            var paths =
                new string[
                    result.Count];
            result.CopyTo(
                paths);
            return paths;
        }

        public static bool Matches(
            string currentPath,
            string sourcePath,
            bool includeDescendants)
        {
            if (!TryNormalize(
                    currentPath,
                    out var current,
                    out _) ||
                !TryNormalize(
                    sourcePath,
                    out var source,
                    out _))
            {
                return false;
            }

            return
                string.Equals(
                    current,
                    source,
                    StringComparison.Ordinal) ||
                (includeDescendants &&
                 current.StartsWith(
                     source + "/",
                     StringComparison.Ordinal));
        }

        public static bool TryRewrite(
            string currentPath,
            string sourcePath,
            string destinationPath,
            bool includeDescendants,
            out string rewritten)
        {
            rewritten = null;

            if (!TryNormalize(
                    currentPath,
                    out var current,
                    out _) ||
                !TryNormalize(
                    sourcePath,
                    out var source,
                    out _) ||
                !TryNormalize(
                    destinationPath,
                    out var destination,
                    out _))
            {
                return false;
            }

            if (string.Equals(
                    current,
                    source,
                    StringComparison.Ordinal))
            {
                rewritten =
                    destination;
                return true;
            }

            if (!includeDescendants ||
                !current.StartsWith(
                    source + "/",
                    StringComparison.Ordinal))
            {
                return false;
            }

            rewritten =
                destination +
                current.Substring(
                    source.Length);
            return true;
        }
    }
}
