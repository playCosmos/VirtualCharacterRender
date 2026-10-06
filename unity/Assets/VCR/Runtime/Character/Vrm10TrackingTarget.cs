using System;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Character
{
    /// <summary>
    /// Applies source-neutral tracking state to one VRM 1.0 runtime instance.
    ///
    /// VRM 0.x models loaded through UniVRM's VRM10 runtime conversion path can
    /// use the same target. Tracking sources never touch VRM objects directly.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class Vrm10TrackingTarget : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Vrm10Instance target;
        [Tooltip("Optional component implementing ITrackingFrameProvider. A future mixer can provide the same interface.")]
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private bool autoFindTrackingProvider = true;

        [Header("Face")]
        [SerializeField] private bool applyHead = true;
        [SerializeField] private bool applyExpressions = true;
        [SerializeField, Range(0f, 1f)] private float headWeight = 1f;
        [SerializeField, Min(0f)] private float headSmoothing = 18f;
        [SerializeField, Min(0f)] private float expressionSmoothing = 22f;

        [Header("Upper body")]
        [SerializeField] private bool applyUpperBody = true;
        [SerializeField, Range(0f, 1f)] private float torsoWeight = 0.65f;
        [SerializeField, Range(0f, 1f)] private float armWeight = 0.9f;
        [SerializeField, Min(0f)] private float bodySmoothing = 14f;
        [SerializeField, Range(0f, 1f)] private float minimumJointConfidence = 0.35f;

        [Header("Tracking loss")]
        [SerializeField] private bool returnToNeutralWhenTrackingUnavailable = true;
        [SerializeField, Min(0f)] private float neutralReturnSmoothing = 8f;

        private ITrackingFrameProvider _provider;
        private ITrackingPresenceProvider _presenceProvider;
        private float _nextProviderSearchTime;

        private Transform _head;
        private Transform _torso;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;

        private Quaternion _headNeutralLocal;
        private Quaternion _headSourceReference;
        private bool _faceCalibrated;

        private BodyReference _bodyReference;
        private bool _bodyCalibrated;
        private bool _hasBodyReference;
        private bool _faceSuppressed;
        private bool _bodySuppressed;
        private bool _fullBodyOverrideActive;

        private NormalizedFaceState _latestFace;
        private NormalizedUpperBodyState _latestBody;
        private TrackingFrame _lastFaceFrame;
        private TrackingFrame _lastBodyFrame;
        private string _lastFaceSourceId;
        private string _lastBodySourceId;

        private float _blinkLeft;
        private float _blinkRight;
        private float _lookUp;
        private float _lookDown;
        private float _lookLeft;
        private float _lookRight;
        private float _aa;
        private float _ih;
        private float _ou;
        private float _ee;
        private float _oh;

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
                Debug.LogError("VCR: Vrm10TrackingTarget requires a Vrm10Instance.", this);
                enabled = false;
                return;
            }

            ResolveProvider();
            CacheBones();
        }

        private void Update()
        {
            if (!IsServiceAlive(_provider))
            {
                _provider = null;
                _presenceProvider = null;

                SetFaceSuppressed(true);
                SetBodySuppressed(true);
                _fullBodyOverrideActive = false;
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

            UpdateRegionAvailability();

            if (!_faceSuppressed &&
                _provider.TryGetLatestFace(out var faceFrame) &&
                faceFrame?.Face != null &&
                !ReferenceEquals(
                    faceFrame,
                    _lastFaceFrame))
            {
                if (!string.Equals(
                    _lastFaceSourceId,
                    faceFrame.SourceId,
                    StringComparison.Ordinal))
                {
                    _faceCalibrated = false;
                    _lastFaceSourceId = faceFrame.SourceId;
                }

                _lastFaceFrame = faceFrame;
                Submit(faceFrame);
            }

            if (!_fullBodyOverrideActive &&
                !_bodySuppressed &&
                _provider.TryGetLatestBodyHands(out var bodyFrame) &&
                bodyFrame != null &&
                !ReferenceEquals(
                    bodyFrame,
                    _lastBodyFrame))
            {
                if (!string.Equals(
                    _lastBodySourceId,
                    bodyFrame.SourceId,
                    StringComparison.Ordinal))
                {
                    _bodyCalibrated = false;
                    _lastBodySourceId = bodyFrame.SourceId;
                }

                _lastBodyFrame = bodyFrame;
                Submit(bodyFrame);
            }
        }

        private void LateUpdate()
        {
            var deltaTime = Time.unscaledDeltaTime;

            if (_faceSuppressed &&
                returnToNeutralWhenTrackingUnavailable)
            {
                ApplyFaceNeutral(deltaTime);
            }
            else if (_latestFace != null)
            {
                ApplyFace(_latestFace, deltaTime);
            }

            if (_fullBodyOverrideActive)
            {
                return;
            }

            if (_bodySuppressed &&
                returnToNeutralWhenTrackingUnavailable)
            {
                ApplyBodyNeutral(deltaTime);
            }
            else if (applyUpperBody &&
                     _latestBody != null)
            {
                ApplyBody(_latestBody, deltaTime);
            }
        }

        public void Submit(TrackingFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            if (frame.Face != null)
            {
                _latestFace = frame.Face;
            }

            if (frame.UpperBody != null)
            {
                _latestBody = frame.UpperBody;
            }
        }

        public void SubmitFace(NormalizedFaceState face)
        {
            _latestFace = face;
        }

        public void SubmitUpperBody(NormalizedUpperBodyState body)
        {
            _latestBody = body;
        }

        [ContextMenu("Recalibrate Tracking")]
        public void Recalibrate()
        {
            _faceCalibrated = false;
            _bodyCalibrated = false;
        }

        public void SetTrackingProvider(ITrackingFrameProvider provider)
        {
            _provider =
                IsServiceAlive(provider)
                    ? provider
                    : null;
            _presenceProvider =
                IsServiceAlive(_provider)
                    ? _provider as ITrackingPresenceProvider
                    : null;
            trackingProviderBehaviour =
                _provider as MonoBehaviour;
            _lastFaceFrame = null;
            _lastBodyFrame = null;
            _lastFaceSourceId = null;
            _lastBodySourceId = null;
            _faceCalibrated = false;
            _bodyCalibrated = false;
            _faceSuppressed = false;
            _bodySuppressed = false;
            _fullBodyOverrideActive = false;
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
                trackingProviderBehaviour is ITrackingFrameProvider configured)
            {
                _provider = configured;
                _presenceProvider = trackingProviderBehaviour as ITrackingPresenceProvider;
                return;
            }

            if (!autoFindTrackingProvider)
            {
                return;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            ITrackingFrameProvider directProvider = null;
            MonoBehaviour directBehaviour = null;

            foreach (var behaviour in behaviours)
            {
                if (behaviour is ITrackingMixProvider mixer)
                {
                    _provider = mixer;
                    _presenceProvider = mixer;
                    trackingProviderBehaviour = behaviour;
                    return;
                }

                if (behaviour is ITrackingRouteProvider route)
                {
                    _provider = route;
                    _presenceProvider = route;
                    trackingProviderBehaviour = behaviour;
                    return;
                }

                if (directProvider == null &&
                    behaviour is ITrackingFrameProvider provider)
                {
                    directProvider = provider;
                    directBehaviour = behaviour;
                }
            }

            if (directProvider != null)
            {
                _provider = directProvider;
                _presenceProvider =
                    directBehaviour as ITrackingPresenceProvider;
                trackingProviderBehaviour = directBehaviour;
            }
        }

        private void CacheBones()
        {
            // UniVRM runtime loading generates a normalized control rig by
            // default. Write tracking to that rig so Runtime.Process() carries
            // the pose into the model instead of overwriting raw-bone edits.
            _head = GetDrivenBone(HumanBodyBones.Head);
            _torso =
                GetDrivenBone(HumanBodyBones.UpperChest) ??
                GetDrivenBone(HumanBodyBones.Chest) ??
                GetDrivenBone(HumanBodyBones.Spine);

            _leftUpperArm = GetDrivenBone(HumanBodyBones.LeftUpperArm);
            _leftLowerArm = GetDrivenBone(HumanBodyBones.LeftLowerArm);
            _rightUpperArm = GetDrivenBone(HumanBodyBones.RightUpperArm);
            _rightLowerArm = GetDrivenBone(HumanBodyBones.RightLowerArm);

            if (_head != null)
            {
                _headNeutralLocal = _head.localRotation;
            }
        }

        private Transform GetDrivenBone(HumanBodyBones bone)
        {
            var controlRigBone = target.Runtime.ControlRig?.GetBoneTransform(bone);
            if (controlRigBone != null)
            {
                return controlRigBone;
            }

            return target.Humanoid.GetBoneTransform(bone);
        }

        private void UpdateRegionAvailability()
        {
            if (!IsServiceAlive(_presenceProvider))
            {
                SetFaceSuppressed(false);
                SetBodySuppressed(false);
                _fullBodyOverrideActive = false;
                return;
            }

            var presence = _presenceProvider.Presence;

            var fullBodyAvailable =
                presence.FullBodySourceAvailable &&
                presence.FullBodySubjectEvidence;

            if (fullBodyAvailable != _fullBodyOverrideActive)
            {
                _fullBodyOverrideActive = fullBodyAvailable;
                _bodyCalibrated = false;

                if (!fullBodyAvailable)
                {
                    _latestBody = null;
                    _lastBodyFrame = null;
                }
            }

            var faceUnavailable =
                presence.SubjectState != SubjectPresenceState.Present ||
                !presence.FaceSourceAvailable ||
                !presence.FaceSubjectEvidence;

            var bodyUnavailable =
                presence.SubjectState != SubjectPresenceState.Present ||
                !presence.BodyHandsSourceAvailable ||
                !presence.BodyHandsSubjectEvidence;

            SetFaceSuppressed(faceUnavailable);
            SetBodySuppressed(bodyUnavailable);
        }

        private void SetFaceSuppressed(bool suppressed)
        {
            if (_faceSuppressed == suppressed)
            {
                return;
            }

            _faceSuppressed = suppressed;
            _faceCalibrated = false;

            if (suppressed)
            {
                _latestFace = null;
                _lastFaceFrame = null;
            }
        }

        private void SetBodySuppressed(bool suppressed)
        {
            if (_bodySuppressed == suppressed)
            {
                return;
            }

            _bodySuppressed = suppressed;
            _bodyCalibrated = false;

            if (suppressed)
            {
                _latestBody = null;
                _lastBodyFrame = null;
            }
        }

        private void ApplyFaceNeutral(float deltaTime)
        {
            var alpha =
                SmoothAlpha(
                    neutralReturnSmoothing,
                    deltaTime);

            if (applyHead && _head != null)
            {
                _head.localRotation =
                    Quaternion.Slerp(
                        _head.localRotation,
                        _headNeutralLocal,
                        alpha);
            }

            if (applyExpressions)
            {
                FadeExpressionsToNeutral(alpha);
            }
        }

        private void ApplyBodyNeutral(float deltaTime)
        {
            if (!applyUpperBody ||
                !_hasBodyReference)
            {
                return;
            }

            var alpha =
                SmoothAlpha(
                    neutralReturnSmoothing,
                    deltaTime);

            if (_torso != null)
            {
                _torso.rotation =
                    Quaternion.Slerp(
                        _torso.rotation,
                        _bodyReference.TorsoWorldRotation,
                        alpha);
            }

            ReturnBoneToReference(
                _leftUpperArm,
                _bodyReference.LeftUpperArmWorldRotation,
                alpha);
            ReturnBoneToReference(
                _leftLowerArm,
                _bodyReference.LeftLowerArmWorldRotation,
                alpha);
            ReturnBoneToReference(
                _rightUpperArm,
                _bodyReference.RightUpperArmWorldRotation,
                alpha);
            ReturnBoneToReference(
                _rightLowerArm,
                _bodyReference.RightLowerArmWorldRotation,
                alpha);
        }

        private void FadeExpressionsToNeutral(float alpha)
        {
            var expression = target.Runtime.Expression;

            _blinkLeft = Mathf.Lerp(_blinkLeft, 0f, alpha);
            _blinkRight = Mathf.Lerp(_blinkRight, 0f, alpha);
            _lookUp = Mathf.Lerp(_lookUp, 0f, alpha);
            _lookDown = Mathf.Lerp(_lookDown, 0f, alpha);
            _lookLeft = Mathf.Lerp(_lookLeft, 0f, alpha);
            _lookRight = Mathf.Lerp(_lookRight, 0f, alpha);
            _aa = Mathf.Lerp(_aa, 0f, alpha);
            _ih = Mathf.Lerp(_ih, 0f, alpha);
            _ou = Mathf.Lerp(_ou, 0f, alpha);
            _ee = Mathf.Lerp(_ee, 0f, alpha);
            _oh = Mathf.Lerp(_oh, 0f, alpha);

            var bilateralBlink =
                Mathf.Min(
                    _blinkLeft,
                    _blinkRight);

            expression.SetWeight(
                ExpressionKey.Blink,
                bilateralBlink);
            expression.SetWeight(
                ExpressionKey.BlinkLeft,
                Mathf.Max(
                    0f,
                    _blinkLeft - bilateralBlink));
            expression.SetWeight(
                ExpressionKey.BlinkRight,
                Mathf.Max(
                    0f,
                    _blinkRight - bilateralBlink));

            expression.SetWeight(
                ExpressionKey.LookUp,
                _lookUp);
            expression.SetWeight(
                ExpressionKey.LookDown,
                _lookDown);
            expression.SetWeight(
                ExpressionKey.LookLeft,
                _lookLeft);
            expression.SetWeight(
                ExpressionKey.LookRight,
                _lookRight);
            expression.SetWeight(
                ExpressionKey.Aa,
                _aa);
            expression.SetWeight(
                ExpressionKey.Ih,
                _ih);
            expression.SetWeight(
                ExpressionKey.Ou,
                _ou);
            expression.SetWeight(
                ExpressionKey.Ee,
                _ee);
            expression.SetWeight(
                ExpressionKey.Oh,
                _oh);
        }

        private static void ReturnBoneToReference(
            Transform bone,
            Quaternion reference,
            float alpha)
        {
            if (bone == null)
            {
                return;
            }

            bone.rotation =
                Quaternion.Slerp(
                    bone.rotation,
                    reference,
                    alpha);
        }

        private void ApplyFace(NormalizedFaceState face, float deltaTime)
        {
            var hasValidHead =
                TryToUnity(
                    face.HeadRotation,
                    out var sourceHead);

            if (hasValidHead)
            {
                if (!_faceCalibrated)
                {
                    _headSourceReference = sourceHead;
                    _faceCalibrated = true;
                }

                if (applyHead && _head != null)
                {
                    var delta =
                        sourceHead *
                        Quaternion.Inverse(
                            _headSourceReference);
                    var desired =
                        _headNeutralLocal *
                        delta;
                    var alpha =
                        SmoothAlpha(
                            headSmoothing,
                            deltaTime) *
                        headWeight;
                    _head.localRotation =
                        Quaternion.Slerp(
                            _head.localRotation,
                            desired,
                            alpha);
                }
            }

            if (!applyExpressions)
            {
                return;
            }

            var expression = target.Runtime.Expression;
            var alphaExpression = SmoothAlpha(expressionSmoothing, deltaTime);

            var rawBlinkLeft = Clamp01(face.Get(FaceCoefficient.EyeBlinkLeft));
            var rawBlinkRight = Clamp01(face.Get(FaceCoefficient.EyeBlinkRight));

            _blinkLeft = Mathf.Lerp(_blinkLeft, rawBlinkLeft, alphaExpression);
            _blinkRight = Mathf.Lerp(_blinkRight, rawBlinkRight, alphaExpression);

            // Avoid double-applying a bilateral blink on models that define both
            // Blink and left/right wink expressions.
            var bilateralBlink = Mathf.Min(_blinkLeft, _blinkRight);
            expression.SetWeight(ExpressionKey.Blink, bilateralBlink);
            expression.SetWeight(ExpressionKey.BlinkLeft, Mathf.Max(0f, _blinkLeft - bilateralBlink));
            expression.SetWeight(ExpressionKey.BlinkRight, Mathf.Max(0f, _blinkRight - bilateralBlink));

            var rawLookUp = Average(
                face.Get(FaceCoefficient.EyeLookUpLeft),
                face.Get(FaceCoefficient.EyeLookUpRight));
            var rawLookDown = Average(
                face.Get(FaceCoefficient.EyeLookDownLeft),
                face.Get(FaceCoefficient.EyeLookDownRight));
            var rawLookLeft = Average(
                face.Get(FaceCoefficient.EyeLookOutLeft),
                face.Get(FaceCoefficient.EyeLookInRight));
            var rawLookRight = Average(
                face.Get(FaceCoefficient.EyeLookInLeft),
                face.Get(FaceCoefficient.EyeLookOutRight));

            _lookUp = Mathf.Lerp(_lookUp, rawLookUp, alphaExpression);
            _lookDown = Mathf.Lerp(_lookDown, rawLookDown, alphaExpression);
            _lookLeft = Mathf.Lerp(_lookLeft, rawLookLeft, alphaExpression);
            _lookRight = Mathf.Lerp(_lookRight, rawLookRight, alphaExpression);

            expression.SetWeight(ExpressionKey.LookUp, _lookUp);
            expression.SetWeight(ExpressionKey.LookDown, _lookDown);
            expression.SetWeight(ExpressionKey.LookLeft, _lookLeft);
            expression.SetWeight(ExpressionKey.LookRight, _lookRight);

            // VRM standard mouth presets are phoneme-oriented, while MediaPipe
            // provides geometric mouth coefficients. These P0 mappings are
            // intentionally conservative heuristics and remain replaceable by a
            // calibrated lip-sync/mixer stage.
            var jaw = face.Get(FaceCoefficient.JawOpen);
            var pucker = face.Get(FaceCoefficient.MouthPucker);
            var funnel = face.Get(FaceCoefficient.MouthFunnel);
            var stretch = Average(
                face.Get(FaceCoefficient.MouthStretchLeft),
                face.Get(FaceCoefficient.MouthStretchRight));
            var smile = Average(
                face.Get(FaceCoefficient.MouthSmileLeft),
                face.Get(FaceCoefficient.MouthSmileRight));

            var rawAa = Clamp01(jaw * (1f - 0.5f * pucker));
            var rawOu = Clamp01(pucker);
            var rawOh = Clamp01(funnel * Mathf.Max(0.35f, jaw));
            var rawIh = Clamp01(stretch * Mathf.Max(0.25f, jaw));
            var rawEe = Clamp01(smile * Mathf.Max(0.2f, 1f - pucker));

            _aa = Mathf.Lerp(_aa, rawAa, alphaExpression);
            _ou = Mathf.Lerp(_ou, rawOu, alphaExpression);
            _oh = Mathf.Lerp(_oh, rawOh, alphaExpression);
            _ih = Mathf.Lerp(_ih, rawIh, alphaExpression);
            _ee = Mathf.Lerp(_ee, rawEe, alphaExpression);

            expression.SetWeight(ExpressionKey.Aa, _aa);
            expression.SetWeight(ExpressionKey.Ih, _ih);
            expression.SetWeight(ExpressionKey.Ou, _ou);
            expression.SetWeight(ExpressionKey.Ee, _ee);
            expression.SetWeight(ExpressionKey.Oh, _oh);
        }

        private void ApplyBody(NormalizedUpperBodyState body, float deltaTime)
        {
            if (!_bodyCalibrated)
            {
                if (!TryBuildBodyReference(body, out _bodyReference))
                {
                    return;
                }

                _bodyCalibrated = true;
                _hasBodyReference = true;
                return;
            }

            var alpha = SmoothAlpha(bodySmoothing, deltaTime);

            if (_torso != null &&
                TryTorsoRotation(body, out var torsoRotation))
            {
                var delta = torsoRotation * Quaternion.Inverse(_bodyReference.SourceTorsoRotation);
                var desired = delta * _bodyReference.TorsoWorldRotation;
                _torso.rotation = Quaternion.Slerp(
                    _torso.rotation,
                    desired,
                    alpha * torsoWeight);
            }

            ApplyLimb(
                body,
                UpperBodyJoint.LeftShoulder,
                UpperBodyJoint.LeftElbow,
                _leftUpperArm,
                _bodyReference.LeftUpperArmDirection,
                _bodyReference.LeftUpperArmWorldRotation,
                alpha * armWeight);

            ApplyLimb(
                body,
                UpperBodyJoint.LeftElbow,
                UpperBodyJoint.LeftWrist,
                _leftLowerArm,
                _bodyReference.LeftLowerArmDirection,
                _bodyReference.LeftLowerArmWorldRotation,
                alpha * armWeight);

            ApplyLimb(
                body,
                UpperBodyJoint.RightShoulder,
                UpperBodyJoint.RightElbow,
                _rightUpperArm,
                _bodyReference.RightUpperArmDirection,
                _bodyReference.RightUpperArmWorldRotation,
                alpha * armWeight);

            ApplyLimb(
                body,
                UpperBodyJoint.RightElbow,
                UpperBodyJoint.RightWrist,
                _rightLowerArm,
                _bodyReference.RightLowerArmDirection,
                _bodyReference.RightLowerArmWorldRotation,
                alpha * armWeight);
        }

        private void ApplyLimb(
            NormalizedUpperBodyState body,
            UpperBodyJoint from,
            UpperBodyJoint to,
            Transform bone,
            Vector3 sourceReferenceDirection,
            Quaternion boneReferenceWorldRotation,
            float alpha)
        {
            if (bone == null ||
                !TryDirection(body, from, to, out var currentDirection))
            {
                return;
            }

            var delta = Quaternion.FromToRotation(
                sourceReferenceDirection,
                currentDirection);

            var desired = delta * boneReferenceWorldRotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, desired, alpha);
        }

        private bool TryBuildBodyReference(
            NormalizedUpperBodyState body,
            out BodyReference reference)
        {
            reference = default;

            if (!TryTorsoRotation(body, out var torsoRotation) ||
                !TryDirection(body, UpperBodyJoint.LeftShoulder, UpperBodyJoint.LeftElbow, out var leftUpper) ||
                !TryDirection(body, UpperBodyJoint.LeftElbow, UpperBodyJoint.LeftWrist, out var leftLower) ||
                !TryDirection(body, UpperBodyJoint.RightShoulder, UpperBodyJoint.RightElbow, out var rightUpper) ||
                !TryDirection(body, UpperBodyJoint.RightElbow, UpperBodyJoint.RightWrist, out var rightLower))
            {
                return false;
            }

            reference = new BodyReference
            {
                SourceTorsoRotation = torsoRotation,
                LeftUpperArmDirection = leftUpper,
                LeftLowerArmDirection = leftLower,
                RightUpperArmDirection = rightUpper,
                RightLowerArmDirection = rightLower,
                TorsoWorldRotation = _torso != null ? _torso.rotation : Quaternion.identity,
                LeftUpperArmWorldRotation = _leftUpperArm != null ? _leftUpperArm.rotation : Quaternion.identity,
                LeftLowerArmWorldRotation = _leftLowerArm != null ? _leftLowerArm.rotation : Quaternion.identity,
                RightUpperArmWorldRotation = _rightUpperArm != null ? _rightUpperArm.rotation : Quaternion.identity,
                RightLowerArmWorldRotation = _rightLowerArm != null ? _rightLowerArm.rotation : Quaternion.identity,
            };

            return true;
        }

        private bool TryTorsoRotation(
            NormalizedUpperBodyState body,
            out Quaternion rotation)
        {
            var leftShoulder = body.Get(UpperBodyJoint.LeftShoulder);
            var rightShoulder = body.Get(UpperBodyJoint.RightShoulder);
            var leftHip = body.Get(UpperBodyJoint.LeftHip);
            var rightHip = body.Get(UpperBodyJoint.RightHip);

            if (!IsUsable(leftShoulder) ||
                !IsUsable(rightShoulder) ||
                !IsUsable(leftHip) ||
                !IsUsable(rightHip))
            {
                rotation = Quaternion.identity;
                return false;
            }

            var ls = ToUnity(leftShoulder.Position);
            var rs = ToUnity(rightShoulder.Position);
            var lh = ToUnity(leftHip.Position);
            var rh = ToUnity(rightHip.Position);

            var right = rs - ls;
            var up = ((ls + rs) * 0.5f) - ((lh + rh) * 0.5f);

            if (right.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f)
            {
                rotation = Quaternion.identity;
                return false;
            }

            right.Normalize();
            up.Normalize();

            var forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                rotation = Quaternion.identity;
                return false;
            }

            forward.Normalize();
            rotation = Quaternion.LookRotation(forward, up);
            return true;
        }

        private bool TryDirection(
            NormalizedUpperBodyState body,
            UpperBodyJoint from,
            UpperBodyJoint to,
            out Vector3 direction)
        {
            var a = body.Get(from);
            var b = body.Get(to);

            if (!IsUsable(a) || !IsUsable(b))
            {
                direction = Vector3.zero;
                return false;
            }

            direction = ToUnity(b.Position) - ToUnity(a.Position);
            if (direction.sqrMagnitude < 1e-6f)
            {
                direction = Vector3.zero;
                return false;
            }

            direction.Normalize();
            return true;
        }

        private bool IsUsable(TrackingPoint point)
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

            return point.Confidence < 0f ||
                   point.Confidence >=
                       minimumJointConfidence;
        }

        private static Vector3 ToUnity(TrackingVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
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

            if (!float.IsFinite(
                    candidate.sqrMagnitude) ||
                candidate.sqrMagnitude <
                    1e-8f)
            {
                return false;
            }

            rotation = candidate;
            return true;
        }

        private void SanitizeConfiguration()
        {
            headWeight =
                SanitizeBounded(
                    headWeight,
                    fallback: 1f,
                    minimum: 0f,
                    maximum: 1f);
            headSmoothing =
                SanitizeNonNegative(
                    headSmoothing,
                    fallback: 18f);
            expressionSmoothing =
                SanitizeNonNegative(
                    expressionSmoothing,
                    fallback: 22f);
            torsoWeight =
                SanitizeBounded(
                    torsoWeight,
                    fallback: 0.65f,
                    minimum: 0f,
                    maximum: 1f);
            armWeight =
                SanitizeBounded(
                    armWeight,
                    fallback: 0.9f,
                    minimum: 0f,
                    maximum: 1f);
            bodySmoothing =
                SanitizeNonNegative(
                    bodySmoothing,
                    fallback: 14f);
            minimumJointConfidence =
                SanitizeBounded(
                    minimumJointConfidence,
                    fallback: 0.35f,
                    minimum: 0f,
                    maximum: 1f);
            neutralReturnSmoothing =
                SanitizeNonNegative(
                    neutralReturnSmoothing,
                    fallback: 8f);
        }

        private static float SanitizeBounded(
            float value,
            float fallback,
            float minimum,
            float maximum)
        {
            if (!float.IsFinite(value))
            {
                value =
                    fallback;
            }

            return Mathf.Clamp(
                value,
                minimum,
                maximum);
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

        private static float SmoothAlpha(float speed, float deltaTime)
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

        private static float Average(float a, float b)
        {
            if (!float.IsFinite(a))
            {
                a = 0f;
            }

            if (!float.IsFinite(b))
            {
                b = 0f;
            }

            return Clamp01((a + b) * 0.5f);
        }

        private static float Clamp01(float value)
        {
            return float.IsFinite(value)
                ? Mathf.Clamp01(value)
                : 0f;
        }

        private struct BodyReference
        {
            public Quaternion SourceTorsoRotation;
            public Vector3 LeftUpperArmDirection;
            public Vector3 LeftLowerArmDirection;
            public Vector3 RightUpperArmDirection;
            public Vector3 RightLowerArmDirection;

            public Quaternion TorsoWorldRotation;
            public Quaternion LeftUpperArmWorldRotation;
            public Quaternion LeftLowerArmWorldRotation;
            public Quaternion RightUpperArmWorldRotation;
            public Quaternion RightLowerArmWorldRotation;
        }
    }
}
