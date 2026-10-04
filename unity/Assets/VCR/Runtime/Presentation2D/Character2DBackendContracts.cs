using System;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Presentation2D
{
    [Flags]
    public enum Character2DInputDomain
    {
        None = 0,
        Face = 1 << 0,
        BodyHands = 1 << 1,
        HumanoidPose = 1 << 2,
        Expressions = 1 << 3
    }

    public enum Character2DBackendState
    {
        Unavailable = 0,
        Ready = 1,
        ModelLoaded = 2,
        Faulted = 3
    }

    public readonly struct Character2DBackendStatus
    {
        public Character2DBackendStatus(
            Character2DBackendState state,
            string modelId,
            string error)
        {
            State = state;
            ModelId = modelId;
            Error = error;
        }

        public Character2DBackendState State { get; }
        public string ModelId { get; }
        public string Error { get; }
    }

    public readonly struct Character2DModelRequest
    {
        public Character2DModelRequest(
            string backendId,
            string modelId,
            string modelPath)
        {
            BackendId = backendId;
            ModelId = modelId;
            ModelPath = modelPath;
        }

        public string BackendId { get; }
        public string ModelId { get; }
        public string ModelPath { get; }
    }

    public readonly struct Character2DInputSnapshot
    {
        public Character2DInputSnapshot(
            TrackingFrame face,
            TrackingFrame bodyHands,
            TrackingFrame humanoidPose,
            TrackingFrame expressions)
        {
            Face = face;
            BodyHands = bodyHands;
            HumanoidPose = humanoidPose;
            Expressions = expressions;
        }

        public TrackingFrame Face { get; }
        public TrackingFrame BodyHands { get; }
        public TrackingFrame HumanoidPose { get; }
        public TrackingFrame Expressions { get; }

        public bool HasAny =>
            Face != null ||
            BodyHands != null ||
            HumanoidPose != null ||
            Expressions != null;
    }

    /// <summary>
    /// Optional 2D presentation backend boundary.
    ///
    /// Implementations live in backend-specific assemblies so the core runtime
    /// never requires Live2D, Inochi2D, or another 2D SDK.
    /// </summary>
    public interface ICharacter2DBackend
    {
        string BackendId { get; }
        Character2DInputDomain SupportedInputs { get; }
        Character2DBackendStatus Status { get; }

        bool TryLoadModel(
            Character2DModelRequest request,
            out string error);

        void UnloadModel();

        bool TryApply(
            Character2DInputSnapshot snapshot,
            out string error);
    }
}
