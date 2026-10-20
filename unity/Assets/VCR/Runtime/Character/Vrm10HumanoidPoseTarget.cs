using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

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
        private const int MaxTrackedCustomExpressions =
            256;
        private const float CustomExpressionPruneThreshold =
            0.0001f;
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

        [Header("Tracking loss")]
        [SerializeField] private bool returnToNeutralWhenUnavailable = true;
        [SerializeField, Min(0f)] private float neutralReturnSmoothing = 8f;

        private ITrackingFrameProvider _provider;
        private ITrackingPresenceProvider _presenceProvider;
        private float _nextProviderSearchTime;

        private readonly Transform[] _bones =
            new Transform[(int)HumanoidBoneId.Count];
        private readonly Quaternion[] _neutralBoneRotations =
            new Quaternion[(int)HumanoidBoneId.Count];
        private readonly bool[] _hasNeutralBone =
            new bool[(int)HumanoidBoneId.Count];
        private Vrm10BonePostureConverter _postureConverter;

        private HumanoidPoseState _latestPose;
        private NormalizedExpressionState _latestExpressions;

        private TrackingFrame _lastPoseFrame;
        private TrackingFrame _lastExpressionFrame;
        private string _lastPoseSourceId;
        private string _lastExpressionSourceId;

        private Vector3 _sourceRootReferencePosition;
        private Quaternion _sourceRootReferenceRotation;
        private Vector3 _targetRootReferencePosition;
        private Quaternion _targetRootReferenceRotation;
        private bool _rootReferenceInitialized;
        private bool _poseUnavailable = true;
        private bool _expressionsUnavailable = true;

        private readonly float[] _smoothedExpressions =
            new float[(int)StandardExpression.Count];

        private readonly Dictionary<string, float> _smoothedCustomExpressions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpressionKey> _customExpressionKeys =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _currentCustomExpressionNames =
            new(StringComparer.Ordinal);
        private readonly List<string> _customExpressionNameScratch =
            new(
                MaxTrackedCustomExpressions);

        private void OnValidate()
        {
            SanitizeConfiguration();
        }

        private void Awake()
        {
            SanitizeConfiguration();

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

            _postureConverter =
                Vrm10BonePostureConverter.Capture(target);
            CacheBones();
            ResolveProvider();
        }

        private void Update()
        {
            if (!IsServiceAlive(_provider))
            {
                _provider = null;
                _presenceProvider = null;
                SetPoseUnavailable(true);
                SetExpressionsUnavailable(true);
            }

            if (_provider == null &&
                Time.unscaledTime >= _nextProviderSearchTime)
            {
                _nextProviderSearchTime =
                    Time.unscaledTime + 1f;
                ResolveProvider();
            }

            if (!IsServiceAlive(_provider))
            {
                return;
            }

            var hasPoseFrame =
                _provider.TryGetLatestHumanoidPose(
                    out var poseFrame) &&
                poseFrame?.HumanoidPose != null;

            var hasExpressionFrame =
                _provider.TryGetLatestExpressions(
                    out var expressionFrame) &&
                expressionFrame?.Expressions != null;

            var presence =
                IsServiceAlive(_presenceProvider)
                    ? _presenceProvider.Presence
                    : (TrackingPresenceSnapshot?)null;

            var availability =
                MotionApplicationAvailabilityResolver
                    .Resolve(
                        presence,
                        hasPoseFrame,
                        hasExpressionFrame,
                        finalMixOwnsPoseAvailability:
                            _provider is ITrackingMixProvider);

            SetPoseUnavailable(
                !availability.PoseAvailable);
            SetExpressionsUnavailable(
                !availability.ExpressionsAvailable);

            if (availability.PoseAvailable &&
                !ReferenceEquals(
                    poseFrame,
                    _lastPoseFrame))
            {
                if (!string.Equals(
                    _lastPoseSourceId,
                    poseFrame.SourceId,
                    StringComparison.Ordinal))
                {
                    ResetPoseCalibration();
                    _lastPoseSourceId =
                        poseFrame.SourceId;
                }

                _lastPoseFrame =
                    poseFrame;
                _latestPose =
                    poseFrame.HumanoidPose;
            }

            if (availability.ExpressionsAvailable &&
                !ReferenceEquals(
                    expressionFrame,
                    _lastExpressionFrame))
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
                    _smoothedCustomExpressions.Clear();
                    _customExpressionKeys.Clear();
                    _currentCustomExpressionNames.Clear();
                    _customExpressionNameScratch.Clear();
                    _lastExpressionSourceId =
                        expressionFrame.SourceId;
                }

                _lastExpressionFrame =
                    expressionFrame;
                _latestExpressions =
                    expressionFrame.Expressions;
            }
        }

        private void LateUpdate()
        {
            var deltaTime =
                Time.unscaledDeltaTime;

            if (applyHumanoidPose)
            {
                if (_poseUnavailable &&
                    returnToNeutralWhenUnavailable)
                {
                    ApplyPoseNeutral(
                        deltaTime);
                }
                else if (_latestPose != null)
                {
                    ApplyPose(
                        _latestPose,
                        deltaTime);
                }
            }

            if (applyExpressions)
            {
                if (_expressionsUnavailable &&
                    returnToNeutralWhenUnavailable)
                {
                    FadeExpressionsToNeutral(
                        SmoothAlpha(
                            neutralReturnSmoothing,
                            deltaTime));
                }
                else if (_latestExpressions != null)
                {
                    ApplyExpressionState(
                        _latestExpressions,
                        deltaTime);
                }
            }
        }

        public void SetTrackingProvider(ITrackingFrameProvider provider)
        {
            _provider =
                IsServiceAlive(provider)
                    ? provider
                    : null;
            _presenceProvider =
                IsServiceAlive(_provider)
                    ? _provider as
                        ITrackingPresenceProvider
                    : null;
            trackingProviderBehaviour =
                _provider as MonoBehaviour;
            _lastPoseFrame = null;
            _lastExpressionFrame = null;
            _lastPoseSourceId = null;
            _lastExpressionSourceId = null;
            _latestPose = null;
            _latestExpressions = null;
            _poseUnavailable = true;
            _expressionsUnavailable = true;
            Array.Clear(
                _smoothedExpressions,
                0,
                _smoothedExpressions.Length);
            _smoothedCustomExpressions.Clear();
            _customExpressionKeys.Clear();
            _currentCustomExpressionNames.Clear();
            _customExpressionNameScratch.Clear();
            ResetPoseCalibration();
        }

        [ContextMenu("Recalibrate Full Body")]
        public void ResetPoseCalibration()
        {
            _rootReferenceInitialized = false;
        }

        private void SetPoseUnavailable(
            bool unavailable)
        {
            if (_poseUnavailable == unavailable)
            {
                return;
            }

            _poseUnavailable = unavailable;

            if (unavailable)
            {
                _latestPose = null;
                return;
            }

            ResetPoseCalibration();
            _lastPoseFrame = null;
        }

        private void SetExpressionsUnavailable(
            bool unavailable)
        {
            if (_expressionsUnavailable ==
                unavailable)
            {
                return;
            }

            _expressionsUnavailable =
                unavailable;

            if (unavailable)
            {
                _latestExpressions = null;
                _lastExpressionFrame = null;
                return;
            }

            _lastExpressionFrame = null;
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
        }

        private void ResolveProvider()
        {
            if (trackingProviderBehaviour != null &&
                trackingProviderBehaviour is
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
                if (behaviour is ITrackingMixProvider mixer)
                {
                    SetTrackingProvider(mixer);
                    trackingProviderBehaviour = behaviour;
                    return;
                }

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
                    var driven =
                        GetDrivenBone(unityBone);

                    _bones[i] = driven;

                    if (driven != null)
                    {
                        _neutralBoneRotations[i] =
                            driven.localRotation;
                        _hasNeutralBone[i] = true;
                    }
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
            HumanoidPoseState pose,
            float deltaTime)
        {
            var smoothingRate =
                _provider is ITrackingMixProvider mix &&
                mix.HumanoidPosePreSmoothed
                    ? 0f
                    : poseSmoothing;

            var alpha = SmoothAlpha(
                smoothingRate,
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

                if (!TryToUnity(
                        sourcePose.LocalRotation,
                        out var sourceRotation))
                {
                    continue;
                }

                var desired =
                    pose.PoseSpace == HumanoidPoseSpace.NormalizedLocal
                        ? sourceRotation
                        : (_postureConverter != null
                            ? _postureConverter.ToNormalizedLocalRotation(
                                boneId,
                                sourceRotation)
                            : sourceRotation);

                if (!IsFinite(
                        desired))
                {
                    continue;
                }

                bone.localRotation =
                    Quaternion.Slerp(
                        bone.localRotation,
                        desired,
                        alpha);
            }
        }

        private void ApplyRoot(
            HumanoidPoseState pose,
            float alpha)
        {
            if (!applyRootPosition &&
                !applyRootRotation)
            {
                return;
            }

            var hasSourcePosition =
                TryToUnity(
                    pose.RootPosition,
                    out var sourcePosition);
            var hasSourceRotation =
                TryToUnity(
                    pose.RootRotation,
                    out var sourceRotation);

            if ((applyRootPosition &&
                 !hasSourcePosition) ||
                (applyRootRotation &&
                 !hasSourceRotation))
            {
                return;
            }

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

        private void ApplyPoseNeutral(float deltaTime)
        {
            var alpha =
                SmoothAlpha(
                    neutralReturnSmoothing,
                    deltaTime);

            if (applyHumanoidPose)
            {
                for (var i = 0;
                     i < (int)HumanoidBoneId.Count;
                     i++)
                {
                    if (!_hasNeutralBone[i])
                    {
                        continue;
                    }

                    var bone = _bones[i];
                    if (bone == null)
                    {
                        continue;
                    }

                    bone.localRotation =
                        Quaternion.Slerp(
                            bone.localRotation,
                            _neutralBoneRotations[i],
                            alpha);
                }

                if (_rootReferenceInitialized)
                {
                    if (applyRootPosition)
                    {
                        target.transform.localPosition =
                            Vector3.Lerp(
                                target.transform.localPosition,
                                _targetRootReferencePosition,
                                alpha);
                    }

                    if (applyRootRotation)
                    {
                        target.transform.localRotation =
                            Quaternion.Slerp(
                                target.transform.localRotation,
                                _targetRootReferenceRotation,
                                alpha);
                    }
                }
            }

        }

        private void FadeExpressionsToNeutral(float alpha)
        {
            var runtime =
                target.Runtime.Expression;

            for (var i = 0;
                 i < (int)StandardExpression.Count;
                 i++)
            {
                _smoothedExpressions[i] =
                    Mathf.Lerp(
                        _smoothedExpressions[i],
                        0f,
                        alpha);

                if (TryExpressionKey(
                    (StandardExpression)i,
                    out var key))
                {
                    runtime.SetWeight(
                        key,
                        _smoothedExpressions[i]);
                }
            }

            FadeCustomExpressionsToNeutral(
                alpha,
                onlyMissingFromCurrentFrame: false);
        }

        private void ApplyExpressionState(
            NormalizedExpressionState state,
            float deltaTime)
        {
            var smoothingRate =
                _provider is ITrackingMixProvider mix &&
                mix.ExpressionsPreSmoothed
                    ? 0f
                    : expressionSmoothing;

            var alpha = SmoothAlpha(
                smoothingRate,
                deltaTime);
            var runtime = target.Runtime.Expression;

            for (var i = 0; i < (int)StandardExpression.Count; i++)
            {
                var expression =
                    (StandardExpression)i;
                var value =
                    Clamp01(
                        state.Get(
                            expression));
                var current =
                    Clamp01(
                        _smoothedExpressions[i]);

                _smoothedExpressions[i] =
                    Mathf.Lerp(
                        current,
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

            _currentCustomExpressionNames.Clear();

            foreach (var custom in state.Custom)
            {
                var name =
                    custom.Name;

                if (string.IsNullOrEmpty(
                        name))
                {
                    continue;
                }

                var tracked =
                    _smoothedCustomExpressions
                        .TryGetValue(
                            name,
                            out var currentValue);

                if (!tracked &&
                    _smoothedCustomExpressions.Count >=
                        MaxTrackedCustomExpressions)
                {
                    continue;
                }

                _currentCustomExpressionNames.Add(
                    name);

                var targetValue =
                    Clamp01(
                        custom.Value);
                var smoothed =
                    Mathf.Lerp(
                        Clamp01(
                            currentValue),
                        targetValue,
                        alpha);

                _smoothedCustomExpressions[
                    name] =
                    smoothed;

                runtime.SetWeight(
                    GetCustomExpressionKey(
                        name),
                    smoothed);
            }

            FadeCustomExpressionsToNeutral(
                alpha,
                onlyMissingFromCurrentFrame: true);
        }

        private void FadeCustomExpressionsToNeutral(
            float alpha,
            bool onlyMissingFromCurrentFrame)
        {
            if (_smoothedCustomExpressions.Count == 0 ||
                target == null ||
                target.Runtime == null)
            {
                return;
            }

            var runtime =
                target.Runtime.Expression;

            if (runtime == null)
            {
                return;
            }

            _customExpressionNameScratch.Clear();

            foreach (var pair in
                     _smoothedCustomExpressions)
            {
                if (!onlyMissingFromCurrentFrame ||
                    !_currentCustomExpressionNames
                        .Contains(
                            pair.Key))
                {
                    _customExpressionNameScratch.Add(
                        pair.Key);
                }
            }

            for (var i = 0;
                 i <
                 _customExpressionNameScratch.Count;
                 i++)
            {
                var name =
                    _customExpressionNameScratch[i];
                var value =
                    Mathf.Lerp(
                        _smoothedCustomExpressions[name],
                        0f,
                        alpha);

                if (value <=
                    CustomExpressionPruneThreshold)
                {
                    value = 0f;
                }

                runtime.SetWeight(
                    GetCustomExpressionKey(
                        name),
                    value);

                if (value <= 0f)
                {
                    _smoothedCustomExpressions.Remove(
                        name);
                    _customExpressionKeys.Remove(
                        name);
                }
                else
                {
                    _smoothedCustomExpressions[
                        name] =
                        value;
                }
            }
        }

        private ExpressionKey GetCustomExpressionKey(
            string name)
        {
            if (_customExpressionKeys.TryGetValue(
                    name,
                    out var key))
            {
                return key;
            }

            key =
                ExpressionKey.CreateCustom(
                    name);
            _customExpressionKeys[
                name] =
                    key;
            return key;
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

        private void SanitizeConfiguration()
        {
            poseSmoothing =
                SanitizeNonNegative(
                    poseSmoothing,
                    fallback: 20f);
            expressionSmoothing =
                SanitizeNonNegative(
                    expressionSmoothing,
                    fallback: 22f);
            neutralReturnSmoothing =
                SanitizeNonNegative(
                    neutralReturnSmoothing,
                    fallback: 8f);
        }

        private static float SanitizeNonNegative(
            float value,
            float fallback)
        {
            if (!float.IsFinite(value))
            {
                return fallback;
            }

            return Mathf.Max(
                0f,
                value);
        }

        private static bool TryToUnity(
            TrackingVector3 value,
            out Vector3 position)
        {
            position = Vector3.zero;

            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z))
            {
                return false;
            }

            position =
                new Vector3(
                    value.X,
                    value.Y,
                    value.Z);
            return true;
        }

        private static bool TryToUnity(
            TrackingQuaternion value,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z) ||
                !float.IsFinite(value.W))
            {
                return false;
            }

            var candidate =
                new Quaternion(
                    value.X,
                    value.Y,
                    value.Z,
                    value.W);

            var magnitudeSquared =
                Quaternion.Dot(
                    candidate,
                    candidate);

            if (!IsFinite(candidate) ||
                !float.IsFinite(
                    magnitudeSquared) ||
                magnitudeSquared <
                    1e-8f)
            {
                return false;
            }

            rotation = candidate;
            return true;
        }

        private static bool IsFinite(
            Quaternion value)
        {
            return
                float.IsFinite(value.x) &&
                float.IsFinite(value.y) &&
                float.IsFinite(value.z) &&
                float.IsFinite(value.w);
        }

        private static float Clamp01(
            float value)
        {
            return float.IsFinite(value)
                ? Mathf.Clamp01(value)
                : 0f;
        }

        private static float SmoothAlpha(
            float speed,
            float deltaTime)
        {
            if (!float.IsFinite(speed))
            {
                return 1f;
            }

            if (speed <= 0f)
            {
                return 1f;
            }

            if (!float.IsFinite(deltaTime) ||
                deltaTime <= 0f)
            {
                return 0f;
            }

            var alpha =
                1f -
                Mathf.Exp(
                    -speed *
                    deltaTime);

            return float.IsFinite(alpha)
                ? Mathf.Clamp01(alpha)
                : 1f;
        }
    }
}
