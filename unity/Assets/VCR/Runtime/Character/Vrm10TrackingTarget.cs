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

        private ITrackingFrameProvider _provider;
        private float _nextProviderSearchTime;

        private Transform _head;
        private Transform _torso;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;

        private Quaternion _headInitialLocal;
        private Quaternion _headSourceReference;
        private bool _faceCalibrated;

        private BodyReference _bodyReference;
        private bool _bodyCalibrated;

        private NormalizedFaceState _latestFace;
        private NormalizedUpperBodyState _latestBody;
        private long _lastFaceSequence = -1;
        private long _lastBodySequence = -1;

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

        private void Awake()
        {
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
            if (_provider == null && Time.unscaledTime >= _nextProviderSearchTime)
            {
                _nextProviderSearchTime = Time.unscaledTime + 1f;
                ResolveProvider();
            }

            if (_provider == null)
            {
                return;
            }

            if (_provider.TryGetLatestFace(out var faceFrame) &&
                faceFrame?.Face != null &&
                faceFrame.Sequence != _lastFaceSequence)
            {
                _lastFaceSequence = faceFrame.Sequence;
                Submit(faceFrame);
            }

            if (_provider.TryGetLatestBodyHands(out var bodyFrame) &&
                bodyFrame != null &&
                bodyFrame.Sequence != _lastBodySequence)
            {
                _lastBodySequence = bodyFrame.Sequence;
                Submit(bodyFrame);
            }
        }

        private void LateUpdate()
        {
            if (_latestFace != null)
            {
                ApplyFace(_latestFace, Time.unscaledDeltaTime);
            }

            if (applyUpperBody && _latestBody != null)
            {
                ApplyBody(_latestBody, Time.unscaledDeltaTime);
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
            _provider = provider;
            trackingProviderBehaviour = provider as MonoBehaviour;
        }

        private void ResolveProvider()
        {
            if (trackingProviderBehaviour is ITrackingFrameProvider configured)
            {
                _provider = configured;
                return;
            }

            if (!autoFindTrackingProvider)
            {
                return;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is ITrackingFrameProvider provider)
                {
                    _provider = provider;
                    trackingProviderBehaviour = behaviour;
                    return;
                }
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
                _headInitialLocal = _head.localRotation;
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

        private void ApplyFace(NormalizedFaceState face, float deltaTime)
        {
            var sourceHead = ToUnity(face.HeadRotation);

            if (!_faceCalibrated)
            {
                _headSourceReference = sourceHead;
                if (_head != null)
                {
                    _headInitialLocal = _head.localRotation;
                }
                _faceCalibrated = true;
            }

            if (applyHead && _head != null)
            {
                var delta = sourceHead * Quaternion.Inverse(_headSourceReference);
                var desired = _headInitialLocal * delta;
                var alpha = SmoothAlpha(headSmoothing, deltaTime) * headWeight;
                _head.localRotation = Quaternion.Slerp(_head.localRotation, desired, alpha);
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
            return point.Confidence < 0f ||
                   point.Confidence >= minimumJointConfidence;
        }

        private static Vector3 ToUnity(TrackingVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static Quaternion ToUnity(TrackingQuaternion value)
        {
            return new Quaternion(value.X, value.Y, value.Z, value.W);
        }

        private static float SmoothAlpha(float speed, float deltaTime)
        {
            if (speed <= 0f)
            {
                return 1f;
            }

            return 1f - Mathf.Exp(-speed * Mathf.Max(0f, deltaTime));
        }

        private static float Average(float a, float b)
        {
            return Clamp01((a + b) * 0.5f);
        }

        private static float Clamp01(float value)
        {
            return Mathf.Clamp01(value);
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
