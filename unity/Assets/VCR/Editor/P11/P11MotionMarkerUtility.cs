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

        public static bool TryApplyMarkers(
            AnimationClip clip,
            IReadOnlyList<BakedMotionCueMarker> markers,
            bool replaceExistingNames,
            out string error)
        {
            error = null;

            if (clip == null)
            {
                error =
                    "AnimationClip is required for marker application.";
                return false;
            }

            var incoming =
                markers ??
                Array.Empty<BakedMotionCueMarker>();
            var incomingNames =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var marker in incoming)
            {
                if (marker == null ||
                    string.IsNullOrWhiteSpace(
                        marker.Name))
                {
                    error =
                        "Every imported motion marker requires a non-empty name.";
                    return false;
                }

                if (float.IsNaN(
                        marker.TimeSeconds) ||
                    float.IsInfinity(
                        marker.TimeSeconds) ||
                    marker.TimeSeconds < 0f ||
                    marker.TimeSeconds >
                        clip.length + 0.0001f)
                {
                    error =
                        $"Motion marker '{marker.Name}' is outside AnimationClip duration.";
                    return false;
                }

                if (!incomingNames.Add(
                        marker.Name))
                {
                    error =
                        $"Imported motion marker set contains duplicate name '{marker.Name}'.";
                    return false;
                }
            }

            var events =
                new List<AnimationEvent>(
                    AnimationUtility.GetAnimationEvents(
                        clip) ??
                    Array.Empty<AnimationEvent>());
            var existingNames =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (var animationEvent in events)
            {
                if (TryGetMarkerName(
                        animationEvent,
                        out var markerName))
                {
                    existingNames.Add(
                        markerName);
                }
            }

            if (!replaceExistingNames)
            {
                foreach (var marker in incoming)
                {
                    if (existingNames.Contains(
                            marker.Name))
                    {
                        error =
                            $"AnimationClip already contains VCR marker '{marker.Name}'.";
                        return false;
                    }
                }
            }
            else
            {
                events.RemoveAll(
                    animationEvent =>
                    {
                        if (!TryGetMarkerName(
                                animationEvent,
                                out var markerName))
                        {
                            return false;
                        }

                        return incomingNames.Contains(
                            markerName);
                    });
            }

            foreach (var marker in incoming)
            {
                events.Add(
                    new AnimationEvent
                    {
                        functionName =
                            MarkerFunctionName,
                        stringParameter =
                            marker.Name,
                        time =
                            Mathf.Clamp(
                                marker.TimeSeconds,
                                0f,
                                clip.length)
                    });
            }

            events.Sort(
                (left, right) =>
                {
                    var time =
                        left.time.CompareTo(
                            right.time);

                    if (time != 0)
                    {
                        return time;
                    }

                    return string.Compare(
                        left.functionName,
                        right.functionName,
                        StringComparison.Ordinal);
                });

            try
            {
                AnimationUtility.SetAnimationEvents(
                    clip,
                    events.ToArray());
                EditorUtility.SetDirty(
                    clip);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "AnimationClip marker application failed: " +
                    exception.Message;
                return false;
            }
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
                    StringComparison.Ordinal))
            {
                markerName =
                    animationEvent.stringParameter?
                        .Trim();

                return
                    !string.IsNullOrWhiteSpace(
                        markerName);
            }

            const string underscorePrefix =
                "VCRMarker_";

            if (function.StartsWith(
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
