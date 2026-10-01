using System;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Character
{
    /// <summary>
    /// Applies normalized full-body humanoid pose and expression snapshots.
    ///
    /// It runs before Vrm10TrackingTarget so face/head tracking can override
    /// VMC head/expression channels when a higher-priority face source exists.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9000)]
    public sealed class Vrm10HumanoidPoseTarget : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Vrm10Instance target;
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private bool autoFindTrackingProvider = true;

        [Header("Pose")]
        [SerializeField] private bool applyHumanoidPose = true;
        [Tooltip("Disabled by default so ARKit/MediaPipe face/head owns the head region.")]
        [SerializeField] private bool applyHeadAndFaceBones = false;
        [SerializeField] private bool applyRootPosition = false;
        [SerializeField] private bool applyRootRotation = false;
        [SerializeField, Min(0f)] private float poseSmoothing = 20f;

        [Header("Expressions")]
        [SerializeField] private bool applyExpressions = true;
        [SerializeField, Min(0f)] private float expressionSmoothing = 22f;

        private ITrackingFrameProvider _provider;
        private ITrackingPresenceProvider _presenceProvider;
        private float _nextProviderSearchTime;

        private readonly Transform[] _bones =
            new Transform[(int)HumanoidBoneId.Count];
        private readonly Quaternion[] _sourceReferenceRotations =
            new Quaternion[(int)HumanoidBoneId.Count];
        private readonly Quaternion[] _targetReferenceRotations =
            new Quaternion[(int)HumanoidBoneId.Count];
        private readonly bool[] _hasReference =
            new bool[(int)HumanoidBoneId.Count];

        private NormalizedHumanoidPose _latestPose;
        private NormalizedExpressionState _latestExpressions;

        private long _lastPoseSequence = -1;
        private long _lastExpressionSequence = -1;
        private string _lastPoseSourceId;
        private string _lastExpressionSourceId;

        private Vector3 _sourceRootReferencePosition;
        private Quaternion _sourceRootReferenceRotation;
        private Vector3 _targetRootReferencePosition;
        private Quaternion _targetRootReferenceRotation;
        private bool _rootReferenceInitialized;

        private readonly float[] _smoothedExpressions =
            new float[(int)StandardExpression.Count];

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<Vrm10Instance>();
            }

            if (target == null)
            {
                Debug.LogError(
                    "VCR: Vrm10HumanoidPoseTarget requires Vrm10Instance.",
                    this);
                enabled = false;
                return;
            }

            CacheBones();
            ResolveProvider();
        }

        private void Update()
        {
            if (_provider == null &&
                Time.unscaledTime >= _nextProviderSearchTime)
            {
                _nextProviderSearchTime =
                    Time.unscaledTime + 1f;
                ResolveProvider();
            }

            if (_provider == null)
            {
                return;
            }

            if (_presenceProvider != null &&
                !_presenceProvider.Presence.FullBodySourceAvailable)
            {
                _latestPose = null;
                _latestExpressions = null;
                return;
            }

            if (_provider.TryGetLatestHumanoidPose(out var poseFrame) &&
                poseFrame?.HumanoidPose != null &&
                poseFrame.Sequence != _lastPoseSequence)
            {
                if (!string.Equals(
                    _lastPoseSourceId,
                    poseFrame.SourceId,
                    StringComparison.Ordinal))
                {
                    ResetPoseCalibration();
                    _lastPoseSourceId = poseFrame.SourceId;
                }

                _lastPoseSequence = poseFrame.Sequence;
                _latestPose = poseFrame.HumanoidPose;
            }

            if (_provider.TryGetLatestExpressions(
                    out var expressionFrame) &&
                expressionFrame?.Expressions != null &&
                expressionFrame.Sequence != _lastExpressionSequence)
            {
                if (!string.Equals(
                    _lastExpressionSourceId,
                    expressionFrame.SourceId,
                    StringComparison.Ordinal))
                {
                    Array.Clear(
                        _smoothedExpressions,
                        0,
                        _smoothedExpressions.Length);
                    _lastExpressionSourceId =
                        expressionFrame.SourceId;
                }

                _lastExpressionSequence =
                    expressionFrame.Sequence;
                _latestExpressions =
                    expressionFrame.Expressions;
            }
        }

        private void LateUpdate()
        {
            var deltaTime = Time.unscaledDeltaTime;

            if (applyHumanoidPose &&
                _latestPose != null)
            {
                ApplyPose(_latestPose, deltaTime);
            }

            if (applyExpressions &&
                _latestExpressions != null)
            {
                ApplyExpressionState(
                    _latestExpressions,
                    deltaTime);
            }
        }

        public void SetTrackingProvider(ITrackingFrameProvider provider)
        {
            _provider = provider;
            _presenceProvider =
                provider as ITrackingPresenceProvider;
            trackingProviderBehaviour =
                provider as MonoBehaviour;
            _lastPoseSequence = -1;
            _lastExpressionSequence = -1;
            _lastPoseSourceId = null;
            _lastExpressionSourceId = null;
            _latestPose = null;
            _latestExpressions = null;
            ResetPoseCalibration();
        }

        [ContextMenu("Recalibrate Full Body")]
        public void ResetPoseCalibration()
        {
            Array.Clear(
                _hasReference,
                0,
                _hasReference.Length);
            _rootReferenceInitialized = false;
        }

        private void ResolveProvider()
        {
            if (trackingProviderBehaviour is
                ITrackingFrameProvider configured)
            {
                SetTrackingProvider(configured);
                return;
            }

            if (!autoFindTrackingProvider)
            {
                return;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            ITrackingFrameProvider direct = null;
            MonoBehaviour directBehaviour = null;

            foreach (var behaviour in behaviours)
            {
                if (behaviour is ITrackingRouteProvider route)
                {
                    SetTrackingProvider(route);
                    trackingProviderBehaviour = behaviour;
                    return;
                }

                if (direct == null &&
                    behaviour is ITrackingFrameProvider provider)
                {
                    direct = provider;
                    directBehaviour = behaviour;
                }
            }

            if (direct != null)
            {
                SetTrackingProvider(direct);
                trackingProviderBehaviour = directBehaviour;
            }
        }

        private void CacheBones()
        {
            for (var i = 0; i < (int)HumanoidBoneId.Count; i++)
            {
                var bone = (HumanoidBoneId)i;
                if (TryMapBone(bone, out var unityBone))
                {
                    _bones[i] = GetDrivenBone(unityBone);
                }
            }
        }

        private Transform GetDrivenBone(HumanBodyBones bone)
        {
            var controlRig =
                target.Runtime.ControlRig?.GetBoneTransform(bone);

            return controlRig != null
                ? controlRig
                : target.Humanoid.GetBoneTransform(bone);
        }

        private void ApplyPose(
            NormalizedHumanoidPose pose,
            float deltaTime)
        {
            var alpha = SmoothAlpha(
                poseSmoothing,
                deltaTime);

            ApplyRoot(pose, alpha);

            for (var i = 0; i < (int)HumanoidBoneId.Count; i++)
            {
                var boneId = (HumanoidBoneId)i;
                if (!applyHeadAndFaceBones &&
                    IsHeadOrFaceBone(boneId))
                {
                    continue;
                }

                var bone = _bones[i];
                if (bone == null ||
                    !pose.TryGet(
                        boneId,
                        out var sourcePose))
                {
                    continue;
                }

                var sourceRotation =
                    ToUnity(sourcePose.LocalRotation);

                if (!_hasReference[i])
                {
                    _sourceReferenceRotations[i] =
                        sourceRotation;
                    _targetReferenceRotations[i] =
                        bone.localRotation;
                    _hasReference[i] = true;
                    continue;
                }

                var delta =
                    Quaternion.Inverse(
                        _sourceReferenceRotations[i]) *
                    sourceRotation;

                var desired =
                    _targetReferenceRotations[i] * delta;

                bone.localRotation =
                    Quaternion.Slerp(
                        bone.localRotation,
                        desired,
                        alpha);
            }
        }

        private void ApplyRoot(
            NormalizedHumanoidPose pose,
            float alpha)
        {
            if (!applyRootPosition &&
                !applyRootRotation)
            {
                return;
            }

            var sourcePosition =
                ToUnity(pose.RootPosition);
            var sourceRotation =
                ToUnity(pose.RootRotation);

            if (!_rootReferenceInitialized)
            {
                _sourceRootReferencePosition =
                    sourcePosition;
                _sourceRootReferenceRotation =
                    sourceRotation;
                _targetRootReferencePosition =
                    target.transform.localPosition;
                _targetRootReferenceRotation =
                    target.transform.localRotation;
                _rootReferenceInitialized = true;
                return;
            }

            if (applyRootPosition)
            {
                var delta =
                    sourcePosition -
                    _sourceRootReferencePosition;

                target.transform.localPosition =
                    Vector3.Lerp(
                        target.transform.localPosition,
                        _targetRootReferencePosition + delta,
                        alpha);
            }

            if (applyRootRotation)
            {
                var delta =
                    Quaternion.Inverse(
                        _sourceRootReferenceRotation) *
                    sourceRotation;

                target.transform.localRotation =
                    Quaternion.Slerp(
                        target.transform.localRotation,
                        _targetRootReferenceRotation * delta,
                        alpha);
            }
        }

        private void ApplyExpressionState(
            NormalizedExpressionState state,
            float deltaTime)
        {
            var alpha = SmoothAlpha(
                expressionSmoothing,
                deltaTime);
            var runtime = target.Runtime.Expression;

            for (var i = 0; i < (int)StandardExpression.Count; i++)
            {
                var expression = (StandardExpression)i;
                var value = Mathf.Clamp01(
                    state.Get(expression));

                _smoothedExpressions[i] =
                    Mathf.Lerp(
                        _smoothedExpressions[i],
                        value,
                        alpha);

                if (TryExpressionKey(
                    expression,
                    out var key))
                {
                    runtime.SetWeight(
                        key,
                        _smoothedExpressions[i]);
                }
            }

            foreach (var custom in state.Custom)
            {
                if (string.IsNullOrEmpty(custom.Name))
                {
                    continue;
                }

                runtime.SetWeight(
                    ExpressionKey.CreateCustom(custom.Name),
                    Mathf.Clamp01(custom.Value));
            }
        }

        private static bool TryExpressionKey(
            StandardExpression expression,
            out ExpressionKey key)
        {
            switch (expression)
            {
                case StandardExpression.Neutral:
                    key = ExpressionKey.Neutral; return true;
                case StandardExpression.Happy:
                    key = ExpressionKey.Happy; return true;
                case StandardExpression.Angry:
                    key = ExpressionKey.Angry; return true;
                case StandardExpression.Sad:
                    key = ExpressionKey.Sad; return true;
                case StandardExpression.Relaxed:
                    key = ExpressionKey.Relaxed; return true;
                case StandardExpression.Surprised:
                    key = ExpressionKey.Surprised; return true;
                case StandardExpression.Aa:
                    key = ExpressionKey.Aa; return true;
                case StandardExpression.Ih:
                    key = ExpressionKey.Ih; return true;
                case StandardExpression.Ou:
                    key = ExpressionKey.Ou; return true;
                case StandardExpression.Ee:
                    key = ExpressionKey.Ee; return true;
                case StandardExpression.Oh:
                    key = ExpressionKey.Oh; return true;
                case StandardExpression.Blink:
                    key = ExpressionKey.Blink; return true;
                case StandardExpression.BlinkLeft:
                    key = ExpressionKey.BlinkLeft; return true;
                case StandardExpression.BlinkRight:
                    key = ExpressionKey.BlinkRight; return true;
                case StandardExpression.LookUp:
                    key = ExpressionKey.LookUp; return true;
                case StandardExpression.LookDown:
                    key = ExpressionKey.LookDown; return true;
                case StandardExpression.LookLeft:
                    key = ExpressionKey.LookLeft; return true;
                case StandardExpression.LookRight:
                    key = ExpressionKey.LookRight; return true;
                default:
                    key = default;
                    return false;
            }
        }

        private static bool IsHeadOrFaceBone(HumanoidBoneId bone)
        {
            return
                bone == HumanoidBoneId.Neck ||
                bone == HumanoidBoneId.Head ||
                bone == HumanoidBoneId.LeftEye ||
                bone == HumanoidBoneId.RightEye ||
                bone == HumanoidBoneId.Jaw;
        }

        private static bool TryMapBone(
            HumanoidBoneId bone,
            out HumanBodyBones unityBone)
        {
            return Enum.TryParse(
                bone.ToString(),
                ignoreCase: false,
                out unityBone) &&
                unityBone != HumanBodyBones.LastBone;
        }

        private static Vector3 ToUnity(TrackingVector3 value)
        {
            return new Vector3(
                value.X,
                value.Y,
                value.Z);
        }

        private static Quaternion ToUnity(
            TrackingQuaternion value)
        {
            return new Quaternion(
                value.X,
                value.Y,
                value.Z,
                value.W);
        }

        private static float SmoothAlpha(
            float speed,
            float deltaTime)
        {
            if (speed <= 0f)
            {
                return 1f;
            }

            return 1f -
                Mathf.Exp(
                    -speed *
                    Mathf.Max(0f, deltaTime));
        }
    }
}
