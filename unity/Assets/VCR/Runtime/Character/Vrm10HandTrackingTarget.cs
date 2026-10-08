using System;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Character
{
    /// <summary>
    /// Applies normalized hand landmarks to VRM hand/finger bones.
    ///
    /// The target consumes the optional hand-only routing contract so specialist
    /// inputs such as Ultraleap can override fingers without changing the
    /// allocation-free upper-body route. MediaPipe remains the hand fallback.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(11000)]
    public sealed class Vrm10HandTrackingTarget : MonoBehaviour
    {
        private const int SegmentCount = 15;

        private static readonly FingerSegmentDefinition[] Segments =
        {
            new(
                HandJoint.ThumbCmc,
                HandJoint.ThumbMcp,
                HumanBodyBones.LeftThumbProximal,
                HumanBodyBones.RightThumbProximal),
            new(
                HandJoint.ThumbMcp,
                HandJoint.ThumbIp,
                HumanBodyBones.LeftThumbIntermediate,
                HumanBodyBones.RightThumbIntermediate),
            new(
                HandJoint.ThumbIp,
                HandJoint.ThumbTip,
                HumanBodyBones.LeftThumbDistal,
                HumanBodyBones.RightThumbDistal),

            new(
                HandJoint.IndexMcp,
                HandJoint.IndexPip,
                HumanBodyBones.LeftIndexProximal,
                HumanBodyBones.RightIndexProximal),
            new(
                HandJoint.IndexPip,
                HandJoint.IndexDip,
                HumanBodyBones.LeftIndexIntermediate,
                HumanBodyBones.RightIndexIntermediate),
            new(
                HandJoint.IndexDip,
                HandJoint.IndexTip,
                HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.RightIndexDistal),

            new(
                HandJoint.MiddleMcp,
                HandJoint.MiddlePip,
                HumanBodyBones.LeftMiddleProximal,
                HumanBodyBones.RightMiddleProximal),
            new(
                HandJoint.MiddlePip,
                HandJoint.MiddleDip,
                HumanBodyBones.LeftMiddleIntermediate,
                HumanBodyBones.RightMiddleIntermediate),
            new(
                HandJoint.MiddleDip,
                HandJoint.MiddleTip,
                HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.RightMiddleDistal),

            new(
                HandJoint.RingMcp,
                HandJoint.RingPip,
                HumanBodyBones.LeftRingProximal,
                HumanBodyBones.RightRingProximal),
            new(
                HandJoint.RingPip,
                HandJoint.RingDip,
                HumanBodyBones.LeftRingIntermediate,
                HumanBodyBones.RightRingIntermediate),
            new(
                HandJoint.RingDip,
                HandJoint.RingTip,
                HumanBodyBones.LeftRingDistal,
                HumanBodyBones.RightRingDistal),

            new(
                HandJoint.LittleMcp,
                HandJoint.LittlePip,
                HumanBodyBones.LeftLittleProximal,
                HumanBodyBones.RightLittleProximal),
            new(
                HandJoint.LittlePip,
                HandJoint.LittleDip,
                HumanBodyBones.LeftLittleIntermediate,
                HumanBodyBones.RightLittleIntermediate),
            new(
                HandJoint.LittleDip,
                HandJoint.LittleTip,
                HumanBodyBones.LeftLittleDistal,
                HumanBodyBones.RightLittleDistal)
        };

        [Header("Target")]
        [SerializeField] private Vrm10Instance target;
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private bool autoFindTrackingProvider = true;

        [Header("Hands")]
        [SerializeField] private bool applyHands = true;
        [SerializeField, Range(0f, 1f)] private float handWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float fingerWeight = 1f;
        [SerializeField, Min(0f)] private float handSmoothing = 22f;
        [SerializeField, Range(0f, 1f)] private float minimumJointConfidence = 0.35f;

        [Header("Tracking loss")]
        [SerializeField] private bool returnToNeutralWhenUnavailable = true;
        [SerializeField, Min(0f)] private float neutralReturnSmoothing = 10f;

        private ITrackingFrameProvider _provider;
        private ITrackingHandFrameProvider _handProvider;
        private ITrackingPresenceProvider _presenceProvider;
        private float _nextProviderSearchTime;

        private HandRig _leftRig;
        private HandRig _rightRig;

        private TrackingFrame _lastHandFrame;
        private string _lastHandSourceId;
        private NormalizedHandState _latestLeft;
        private NormalizedHandState _latestRight;
        private bool _leftAvailable;
        private bool _rightAvailable;

        private void OnValidate()
        {
            SanitizeConfiguration();
        }

        private void Awake()
        {
            SanitizeConfiguration();

            if (target == null)
            {
                target =
                    GetComponent<Vrm10Instance>();
            }

            if (target == null)
            {
                Debug.LogError(
                    "VCR: Vrm10HandTrackingTarget requires Vrm10Instance.",
                    this);
                enabled = false;
                return;
            }

            CacheRigs();
            ResolveProvider();
        }

        private void Update()
        {
            if (!IsServiceAlive(_provider))
            {
                ClearProvider();
            }

            if (_provider == null &&
                Time.unscaledTime >=
                    _nextProviderSearchTime)
            {
                _nextProviderSearchTime =
                    Time.unscaledTime +
                    1f;
                ResolveProvider();
            }

            if (!IsServiceAlive(_provider))
            {
                SetHandAvailability(
                    left: false,
                    right: false);
                return;
            }

            TrackingFrame frame = null;

            var hasFrame =
                IsServiceAlive(_handProvider)
                    ? _handProvider.TryGetLatestHands(
                        out frame)
                    : _provider.TryGetLatestBodyHands(
                        out frame);

            if (!hasFrame ||
                frame == null ||
                (!frame.SubjectDetected &&
                 frame.LeftHand == null &&
                 frame.RightHand == null))
            {
                SetHandAvailability(
                    left: false,
                    right: false);
                return;
            }

            var sourceChanged =
                !string.Equals(
                    _lastHandSourceId,
                    frame.SourceId,
                    StringComparison.Ordinal);

            if (sourceChanged)
            {
                ResetCalibration();
                _lastHandSourceId =
                    frame.SourceId;
            }

            if (ReferenceEquals(
                    _lastHandFrame,
                    frame))
            {
                return;
            }

            _lastHandFrame =
                frame;
            _latestLeft =
                frame.LeftHand;
            _latestRight =
                frame.RightHand;

            SetHandAvailability(
                _latestLeft != null,
                _latestRight != null);
        }

        private void LateUpdate()
        {
            if (!applyHands)
            {
                return;
            }

            var deltaTime =
                Time.unscaledDeltaTime;

            ApplyOrReleaseHand(
                _leftRig,
                _latestLeft,
                _leftAvailable,
                deltaTime);
            ApplyOrReleaseHand(
                _rightRig,
                _latestRight,
                _rightAvailable,
                deltaTime);
        }

        public void SetTrackingProvider(
            ITrackingFrameProvider provider)
        {
            _provider =
                IsServiceAlive(provider)
                    ? provider
                    : null;
            _handProvider =
                _provider as
                    ITrackingHandFrameProvider;
            _presenceProvider =
                _provider as
                    ITrackingPresenceProvider;
            trackingProviderBehaviour =
                _provider as
                    MonoBehaviour;

            _lastHandFrame = null;
            _lastHandSourceId = null;
            _latestLeft = null;
            _latestRight = null;
            _leftAvailable = false;
            _rightAvailable = false;
            ResetCalibration();
        }

        [ContextMenu("Recalibrate Hands")]
        public void RecalibrateHands()
        {
            ResetCalibration();
            _lastHandFrame = null;
        }

        private void ApplyOrReleaseHand(
            HandRig rig,
            NormalizedHandState hand,
            bool available,
            float deltaTime)
        {
            if (rig == null)
            {
                return;
            }

            if (!available ||
                hand == null)
            {
                if (returnToNeutralWhenUnavailable &&
                    !HasHealthyFullBodyOverride())
                {
                    ReturnRigToNeutral(
                        rig,
                        SmoothAlpha(
                            neutralReturnSmoothing,
                            deltaTime));
                }

                return;
            }

            if (!rig.Calibrated)
            {
                TryCalibrateRig(
                    rig,
                    hand);
                return;
            }

            var alpha =
                SmoothAlpha(
                    handSmoothing,
                    deltaTime);

            ApplyPalm(
                rig,
                hand,
                alpha * handWeight);

            for (var i = 0;
                 i < SegmentCount;
                 i++)
            {
                ApplySegment(
                    rig,
                    hand,
                    i,
                    alpha * fingerWeight);
            }
        }

        private bool TryCalibrateRig(
            HandRig rig,
            NormalizedHandState hand)
        {
            if (!TryPalmRotation(
                    hand,
                    out var palmRotation))
            {
                return false;
            }

            rig.SourcePalmReference =
                palmRotation;
            rig.PalmCalibrated =
                rig.Palm != null;

            var anySegment = false;

            for (var i = 0;
                 i < SegmentCount;
                 i++)
            {
                var definition =
                    Segments[i];

                if (rig.Segments[i] == null ||
                    !TryDirection(
                        hand,
                        definition.From,
                        definition.To,
                        out var direction))
                {
                    rig.SegmentCalibrated[i] =
                        false;
                    continue;
                }

                rig.SourceDirectionReference[i] =
                    direction;
                rig.SegmentCalibrated[i] =
                    true;
                anySegment = true;
            }

            rig.Calibrated =
                rig.PalmCalibrated ||
                anySegment;

            return rig.Calibrated;
        }

        private void ApplyPalm(
            HandRig rig,
            NormalizedHandState hand,
            float alpha)
        {
            if (!rig.PalmCalibrated ||
                rig.Palm == null ||
                alpha <= 0f ||
                !TryPalmRotation(
                    hand,
                    out var current))
            {
                return;
            }

            var delta =
                current *
                Quaternion.Inverse(
                    rig.SourcePalmReference);

            var baseWorld =
                rig.Palm.parent != null
                    ? rig.Palm.parent.rotation *
                      rig.PalmNeutralLocal
                    : rig.PalmNeutralLocal;

            var desired =
                delta *
                baseWorld;

            if (!IsFinite(
                    desired))
            {
                return;
            }

            rig.Palm.rotation =
                Quaternion.Slerp(
                    rig.Palm.rotation,
                    desired,
                    Mathf.Clamp01(
                        alpha));
        }

        private void ApplySegment(
            HandRig rig,
            NormalizedHandState hand,
            int index,
            float alpha)
        {
            var bone =
                rig.Segments[index];

            if (bone == null ||
                !rig.SegmentCalibrated[index] ||
                alpha <= 0f)
            {
                return;
            }

            var definition =
                Segments[index];

            if (!TryDirection(
                    hand,
                    definition.From,
                    definition.To,
                    out var current))
            {
                return;
            }

            var delta =
                Quaternion.FromToRotation(
                    rig.SourceDirectionReference[index],
                    current);

            var baseWorld =
                bone.parent != null
                    ? bone.parent.rotation *
                      rig.SegmentNeutralLocal[index]
                    : rig.SegmentNeutralLocal[index];

            var desired =
                delta *
                baseWorld;

            if (!IsFinite(
                    desired))
            {
                return;
            }

            bone.rotation =
                Quaternion.Slerp(
                    bone.rotation,
                    desired,
                    Mathf.Clamp01(
                        alpha));
        }

        private void ReturnRigToNeutral(
            HandRig rig,
            float alpha)
        {
            if (alpha <= 0f)
            {
                return;
            }

            if (rig.Palm != null)
            {
                rig.Palm.localRotation =
                    Quaternion.Slerp(
                        rig.Palm.localRotation,
                        rig.PalmNeutralLocal,
                        alpha);
            }

            for (var i = 0;
                 i < SegmentCount;
                 i++)
            {
                var bone =
                    rig.Segments[i];

                if (bone == null)
                {
                    continue;
                }

                bone.localRotation =
                    Quaternion.Slerp(
                        bone.localRotation,
                        rig.SegmentNeutralLocal[i],
                        alpha);
            }
        }

        private void CacheRigs()
        {
            _leftRig =
                BuildRig(
                    isLeft: true);
            _rightRig =
                BuildRig(
                    isLeft: false);
        }

        private HandRig BuildRig(
            bool isLeft)
        {
            var rig =
                new HandRig(
                    isLeft,
                    GetDrivenBone(
                        isLeft
                            ? HumanBodyBones.LeftHand
                            : HumanBodyBones.RightHand));

            for (var i = 0;
                 i < SegmentCount;
                 i++)
            {
                rig.Segments[i] =
                    GetDrivenBone(
                        isLeft
                            ? Segments[i].LeftBone
                            : Segments[i].RightBone);

                if (rig.Segments[i] != null)
                {
                    rig.SegmentNeutralLocal[i] =
                        rig.Segments[i]
                            .localRotation;
                }
            }

            if (rig.Palm != null)
            {
                rig.PalmNeutralLocal =
                    rig.Palm.localRotation;
            }

            return rig;
        }

        private Transform GetDrivenBone(
            HumanBodyBones bone)
        {
            var controlRig =
                target.Runtime.ControlRig
                    ?.GetBoneTransform(
                        bone);

            return controlRig != null
                ? controlRig
                : target.Humanoid
                    .GetBoneTransform(
                        bone);
        }

        private bool TryPalmRotation(
            NormalizedHandState hand,
            out Quaternion rotation)
        {
            rotation =
                Quaternion.identity;

            var wrist =
                hand.Get(
                    HandJoint.Wrist);
            var index =
                hand.Get(
                    HandJoint.IndexMcp);
            var middle =
                hand.Get(
                    HandJoint.MiddleMcp);
            var little =
                hand.Get(
                    HandJoint.LittleMcp);

            if (!IsUsable(wrist) ||
                !IsUsable(index) ||
                !IsUsable(middle) ||
                !IsUsable(little))
            {
                return false;
            }

            var wristPosition =
                ToUnity(
                    wrist.Position);
            var indexPosition =
                ToUnity(
                    index.Position);
            var middlePosition =
                ToUnity(
                    middle.Position);
            var littlePosition =
                ToUnity(
                    little.Position);

            var forward =
                middlePosition -
                wristPosition;
            var across =
                littlePosition -
                indexPosition;

            if (forward.sqrMagnitude <
                    1e-8f ||
                across.sqrMagnitude <
                    1e-8f)
            {
                return false;
            }

            forward.Normalize();
            across.Normalize();

            var normal =
                Vector3.Cross(
                    across,
                    forward);

            if (normal.sqrMagnitude <
                1e-8f)
            {
                return false;
            }

            normal.Normalize();

            rotation =
                Quaternion.LookRotation(
                    forward,
                    normal);

            return IsFinite(
                rotation);
        }

        private bool TryDirection(
            NormalizedHandState hand,
            HandJoint from,
            HandJoint to,
            out Vector3 direction)
        {
            direction =
                Vector3.zero;

            var a =
                hand.Get(
                    from);
            var b =
                hand.Get(
                    to);

            if (!IsUsable(a) ||
                !IsUsable(b))
            {
                return false;
            }

            direction =
                ToUnity(
                    b.Position) -
                ToUnity(
                    a.Position);

            if (direction.sqrMagnitude <
                1e-8f)
            {
                direction =
                    Vector3.zero;
                return false;
            }

            direction.Normalize();
            return true;
        }

        private bool IsUsable(
            TrackingPoint point)
        {
            if (!float.IsFinite(
                    point.Position.X) ||
                !float.IsFinite(
                    point.Position.Y) ||
                !float.IsFinite(
                    point.Position.Z) ||
                !float.IsFinite(
                    point.Confidence))
            {
                return false;
            }

            return
                point.Confidence < 0f ||
                point.Confidence >=
                    minimumJointConfidence;
        }

        private bool HasHealthyFullBodyOverride()
        {
            if (!IsServiceAlive(
                    _presenceProvider))
            {
                return false;
            }

            var presence =
                _presenceProvider.Presence;

            return
                presence.FullBodySourceAvailable &&
                presence.FullBodySubjectEvidence;
        }

        private void SetHandAvailability(
            bool left,
            bool right)
        {
            if (_leftAvailable &&
                !left)
            {
                _leftRig?.ResetCalibration();
                _latestLeft = null;
            }

            if (_rightAvailable &&
                !right)
            {
                _rightRig?.ResetCalibration();
                _latestRight = null;
            }

            _leftAvailable =
                left;
            _rightAvailable =
                right;
        }

        private void ResetCalibration()
        {
            _leftRig?.ResetCalibration();
            _rightRig?.ResetCalibration();
        }

        private void ClearProvider()
        {
            _provider = null;
            _handProvider = null;
            _presenceProvider = null;
            _lastHandFrame = null;
            _lastHandSourceId = null;
            _latestLeft = null;
            _latestRight = null;
            SetHandAvailability(
                left: false,
                right: false);
        }

        private void ResolveProvider()
        {
            if (trackingProviderBehaviour != null &&
                trackingProviderBehaviour is
                    ITrackingFrameProvider configured)
            {
                SetTrackingProvider(
                    configured);
                return;
            }

            if (!autoFindTrackingProvider)
            {
                return;
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            ITrackingFrameProvider direct =
                null;
            MonoBehaviour directBehaviour =
                null;

            foreach (var behaviour in
                     behaviours)
            {
                if (behaviour is
                    ITrackingMixProvider mixer)
                {
                    SetTrackingProvider(
                        mixer);
                    trackingProviderBehaviour =
                        behaviour;
                    return;
                }

                if (behaviour is
                    ITrackingRouteProvider route)
                {
                    SetTrackingProvider(
                        route);
                    trackingProviderBehaviour =
                        behaviour;
                    return;
                }

                if (direct == null &&
                    behaviour is
                        ITrackingFrameProvider provider)
                {
                    direct =
                        provider;
                    directBehaviour =
                        behaviour;
                }
            }

            if (direct != null)
            {
                SetTrackingProvider(
                    direct);
                trackingProviderBehaviour =
                    directBehaviour;
            }
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return
                service is UnityEngine.Object unityObject
                    ? unityObject != null
                    : true;
        }

        private void SanitizeConfiguration()
        {
            handWeight =
                SanitizeBounded(
                    handWeight,
                    1f);
            fingerWeight =
                SanitizeBounded(
                    fingerWeight,
                    1f);
            handSmoothing =
                SanitizeNonNegative(
                    handSmoothing,
                    22f);
            minimumJointConfidence =
                SanitizeBounded(
                    minimumJointConfidence,
                    0.35f);
            neutralReturnSmoothing =
                SanitizeNonNegative(
                    neutralReturnSmoothing,
                    10f);
        }

        private static float SanitizeBounded(
            float value,
            float fallback)
        {
            if (!float.IsFinite(
                    value))
            {
                value =
                    fallback;
            }

            return Mathf.Clamp01(
                value);
        }

        private static float SanitizeNonNegative(
            float value,
            float fallback)
        {
            if (!float.IsFinite(
                    value))
            {
                return fallback;
            }

            return Mathf.Max(
                0f,
                value);
        }

        private static float SmoothAlpha(
            float speed,
            float deltaTime)
        {
            if (!float.IsFinite(
                    speed))
            {
                return 1f;
            }

            if (speed <= 0f)
            {
                return 1f;
            }

            if (!float.IsFinite(
                    deltaTime) ||
                deltaTime <= 0f)
            {
                return 0f;
            }

            var alpha =
                1f -
                Mathf.Exp(
                    -speed *
                    deltaTime);

            return float.IsFinite(
                    alpha)
                ? Mathf.Clamp01(
                    alpha)
                : 1f;
        }

        private static Vector3 ToUnity(
            TrackingVector3 value)
        {
            return
                new Vector3(
                    value.X,
                    value.Y,
                    value.Z);
        }

        private static bool IsFinite(
            Quaternion value)
        {
            return
                float.IsFinite(
                    value.x) &&
                float.IsFinite(
                    value.y) &&
                float.IsFinite(
                    value.z) &&
                float.IsFinite(
                    value.w) &&
                Quaternion.Dot(
                    value,
                    value) >
                    1e-8f;
        }

        private sealed class HandRig
        {
            public HandRig(
                bool isLeft,
                Transform palm)
            {
                IsLeft =
                    isLeft;
                Palm =
                    palm;
                Segments =
                    new Transform[
                        SegmentCount];
                SegmentNeutralLocal =
                    new Quaternion[
                        SegmentCount];
                SourceDirectionReference =
                    new Vector3[
                        SegmentCount];
                SegmentCalibrated =
                    new bool[
                        SegmentCount];
            }

            public bool IsLeft { get; }
            public Transform Palm { get; }
            public Transform[] Segments { get; }
            public Quaternion[] SegmentNeutralLocal { get; }
            public Vector3[] SourceDirectionReference { get; }
            public bool[] SegmentCalibrated { get; }
            public Quaternion PalmNeutralLocal { get; set; }
            public Quaternion SourcePalmReference { get; set; }
            public bool PalmCalibrated { get; set; }
            public bool Calibrated { get; set; }

            public void ResetCalibration()
            {
                PalmCalibrated =
                    false;
                Calibrated =
                    false;
                Array.Clear(
                    SegmentCalibrated,
                    0,
                    SegmentCalibrated.Length);
            }
        }

        private readonly struct FingerSegmentDefinition
        {
            public FingerSegmentDefinition(
                HandJoint from,
                HandJoint to,
                HumanBodyBones leftBone,
                HumanBodyBones rightBone)
            {
                From =
                    from;
                To =
                    to;
                LeftBone =
                    leftBone;
                RightBone =
                    rightBone;
            }

            public HandJoint From { get; }
            public HandJoint To { get; }
            public HumanBodyBones LeftBone { get; }
            public HumanBodyBones RightBone { get; }
        }
    }
}
