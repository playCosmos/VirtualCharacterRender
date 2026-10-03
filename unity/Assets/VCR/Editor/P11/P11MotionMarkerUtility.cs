using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    internal static class P11MotionMarkerUtility
    {
        public const string MarkerFunctionName =
            "VCRMarker";

        public static bool TryExtractFromAnimationClip(
            AnimationClip clip,
            out BakedMotionCueMarker[] markers,
            out string error)
        {
            markers =
                Array.Empty<BakedMotionCueMarker>();
            error = null;

            if (clip == null)
            {
                error =
                    "AnimationClip is required for marker extraction.";
                return false;
            }

            AnimationEvent[] events;

            try
            {
                events =
                    AnimationUtility.GetAnimationEvents(
                        clip) ??
                    Array.Empty<AnimationEvent>();
            }
            catch (Exception exception)
            {
                error =
                    "AnimationClip marker extraction failed: " +
                    exception.Message;
                return false;
            }

            var result =
                new List<BakedMotionCueMarker>();
            var names =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var animationEvent in events)
            {
                if (!TryGetMarkerName(
                        animationEvent,
                        out var markerName))
                {
                    continue;
                }

                if (float.IsNaN(
                        animationEvent.time) ||
                    float.IsInfinity(
                        animationEvent.time) ||
                    animationEvent.time < 0f ||
                    animationEvent.time >
                        clip.length + 0.0001f)
                {
                    error =
                        $"AnimationClip marker '{markerName}' has an invalid time.";
                    return false;
                }

                if (!names.Add(
                        markerName))
                {
                    error =
                        $"AnimationClip contains duplicate VCR marker '{markerName}'.";
                    return false;
                }

                result.Add(
                    new BakedMotionCueMarker
                    {
                        Name =
                            markerName,
                        TimeSeconds =
                            Mathf.Clamp(
                                animationEvent.time,
                                0f,
                                clip.length)
                    });
            }

            result.Sort(
                (left, right) =>
                {
                    var time =
                        left.TimeSeconds.CompareTo(
                            right.TimeSeconds);

                    return time != 0
                        ? time
                        : string.Compare(
                            left.Name,
                            right.Name,
                            StringComparison.Ordinal);
                });

            markers =
                result.ToArray();
            return true;
        }

        public static AppearanceTransitionMarker[]
            ToAppearanceMarkers(
                IReadOnlyList<BakedMotionCueMarker> markers)
        {
            var result =
                new AppearanceTransitionMarker[
                    markers?.Count ?? 0];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                var marker =
                    markers[i];

                result[i] =
                    marker == null
                        ? null
                        : new AppearanceTransitionMarker
                        {
                            Name =
                                marker.Name,
                            TimeSeconds =
                                marker.TimeSeconds
                        };
            }

            return result;
        }

        private static bool TryGetMarkerName(
            AnimationEvent animationEvent,
            out string markerName)
        {
            markerName = null;

            if (animationEvent == null ||
                string.IsNullOrWhiteSpace(
                    animationEvent.functionName))
            {
                return false;
            }

            var function =
                animationEvent.functionName.Trim();

            if (string.Equals(
                    function,
                    MarkerFunctionName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    function,
                    "VCR.Marker",
                    StringComparison.Ordinal))
            {
                markerName =
                    animationEvent.stringParameter?
                        .Trim();

                return
                    !string.IsNullOrWhiteSpace(
                        markerName);
            }

            const string colonPrefix =
                "VCRMarker:";
            const string underscorePrefix =
                "VCRMarker_";

            if (function.StartsWith(
                    colonPrefix,
                    StringComparison.Ordinal))
            {
                markerName =
                    function.Substring(
                            colonPrefix.Length)
                        .Trim();
            }
            else if (function.StartsWith(
                         underscorePrefix,
                         StringComparison.Ordinal))
            {
                markerName =
                    function.Substring(
                            underscorePrefix.Length)
                        .Trim();
            }

            return
                !string.IsNullOrWhiteSpace(
                    markerName);
        }
    }
}
