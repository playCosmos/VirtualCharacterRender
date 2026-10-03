using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking.Mixing
{
    public readonly struct MotionCueStatus
    {
        public MotionCueStatus(
            string runtimeId,
            bool playing,
            string cueId,
            string lastError)
        {
            RuntimeId = runtimeId;
            Playing = playing;
            CueId = cueId;
            LastError = lastError;
        }

        public string RuntimeId { get; }
        public bool Playing { get; }
        public string CueId { get; }
        public string LastError { get; }
    }

    public interface IMotionCueRuntime
    {
        MotionCueStatus Status { get; }
        IReadOnlyList<string> CueIds { get; }

        bool TryPlayCue(
            string cueId,
            out string error);

        bool TryReleaseCue(
            string cueId,
            out string error);

        bool TrySampleCue(
            string cueId,
            float normalizedTime,
            out HumanoidPoseState pose,
            out string error);
    }
}
