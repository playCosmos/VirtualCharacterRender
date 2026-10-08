using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Leap;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.Ultraleap
{
    /// <summary>
    /// VCR adapter for Ultraleap Tracking 7.x.
    ///
    /// The Ultraleap package remains isolated behind VCR's source-neutral
    /// tracking contracts. This runner publishes hand landmarks only; face and
    /// upper-body ownership remain with ARKit/MediaPipe routing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LeapServiceProvider))]
    public sealed class UltraleapTrackingRunner :
        MonoBehaviour,
        ITrackingFrameProvider,
        ITrackingPresenceProvider,
        ITrackingSourceHealthProvider,
        ITrackingRuntimeControl,
        ITrackingRuntimeConfigurable,
        IRuntimeMetricsSource
    {
        private const string SourceId =
            "ultraleap-hands";

        [Header("Provider")]
        [SerializeField] private LeapServiceProvider provider;
        [SerializeField] private string serviceIp =
            "127.0.0.1";
        [SerializeField] private int servicePort =
            12345;
        [SerializeField] private LeapServiceProvider
            .TrackingOptimizationMode trackingMode =
                LeapServiceProvider
                    .TrackingOptimizationMode.Desktop;

        [Header("Tracking space")]
        [SerializeField] private Vector3 localPositionOffset =
            Vector3.zero;
        [SerializeField] private Vector3 localEulerOffset =
            Vector3.zero;

        [Header("Health")]
        [SerializeField, Min(0.1f)] private float
            sourceStaleSeconds = 1.0f;

        private TrackingFrame _latestFrame;
        private TrackingSourceHealth _health =
            new(
                TrackingSourceHealthState.Stopped,
                0,
                float.NaN,
                null);
        private TrackingPresenceSnapshot _presence;
        private string _lastError;
        private long _presenceSequence;
        private long _normalizedSequence;
        private long _enabledAtUs;
        private long _lastFrameRuntimeUs;
        private int _trackedHandCount;
        private bool _subscribed;

        public string ControlId =>
            "ultraleap-hands";

        public string DisplayName =>
            "Ultraleap / Leap Motion";

        public bool ControlEnabled =>
            enabled;

        public TrackingSourceHealthState
            ControlHealthState =>
                _health.State;

        public string ControlError =>
            _lastError;

        public string ConfigurationTitle =>
            "Ultraleap / Leap Motion 설정";

        public bool OpenConfigurationOnAdd =>
            true;

        public TrackingPresenceSnapshot Presence =>
            _presence;

        private void Awake()
        {
            ResolveProvider();
            ApplyTrackingSpace();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveProvider();
            _lastError = null;
            _enabledAtUs =
                MonotonicClock.NowMicroseconds();
            _health =
                new TrackingSourceHealth(
                    TrackingSourceHealthState.Starting,
                    0,
                    float.NaN,
                    null);

            SubscribeProvider();
            ApplyProviderConfiguration();

            if (provider != null)
            {
                provider.enabled = true;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying ||
                provider == null)
            {
                return;
            }

            var nowUs =
                MonotonicClock.NowMicroseconds();

            if (_lastFrameRuntimeUs > 0 &&
                TrackingTimestampMath
                    .TryElapsedMicroseconds(
                        nowUs,
                        _lastFrameRuntimeUs,
                        out var elapsedUs) &&
                elapsedUs <=
                    (ulong)(
                        Math.Max(
                            0.1f,
                            sourceStaleSeconds) *
                        1_000_000.0f))
            {
                return;
            }

            try
            {
                if (provider.TrackingDataSource ==
                    Leap.TrackingSource.NONE)
                {
                    _health =
                        new TrackingSourceHealth(
                            TrackingSourceHealthState.SourceLost,
                            _lastFrameRuntimeUs,
                            float.NaN,
                            null);
                    UpdatePresence(
                        nowUs,
                        sourceAvailable: false,
                        subjectEvidence: false);
                }
                else if (_lastFrameRuntimeUs == 0 &&
                         nowUs - _enabledAtUs >
                             (long)(
                                 Math.Max(
                                     0.1f,
                                     sourceStaleSeconds) *
                                 1_000_000.0f))
                {
                    _health =
                        new TrackingSourceHealth(
                            TrackingSourceHealthState.Degraded,
                            0,
                            float.NaN,
                            null);
                    UpdatePresence(
                        nowUs,
                        sourceAvailable: true,
                        subjectEvidence: false);
                }
            }
            catch (Exception exception)
            {
                SetFault(
                    "Ultraleap service state check failed: " +
                    exception.Message);
            }
        }

        private void OnDisable()
        {
            UnsubscribeProvider();

            if (provider != null)
            {
                provider.enabled = false;
            }

            _latestFrame = null;
            _trackedHandCount = 0;
            _lastFrameRuntimeUs = 0;
            _health =
                new TrackingSourceHealth(
                    TrackingSourceHealthState.Stopped,
                    0,
                    float.NaN,
                    null);
            _presence = default;
        }

        private void OnDestroy()
        {
            UnsubscribeProvider();
        }

        public bool TrySetControlEnabled(
            bool enabledValue,
            out string error)
        {
            error = null;
            enabled = enabledValue;

            if (enabledValue &&
                !enabled)
            {
                error =
                    _lastError ??
                    "Ultraleap tracking could not be enabled.";
                return false;
            }

            return true;
        }

        public bool TryRecover(
            out string error)
        {
            error = null;

            if (!Application.isPlaying)
            {
                error =
                    "Ultraleap recovery requires play mode.";
                return false;
            }

            try
            {
                if (!enabled)
                {
                    enabled = true;
                    return enabled;
                }

                UnsubscribeProvider();

                if (provider != null)
                {
                    provider.enabled = false;
                    provider.enabled = true;
                }

                SubscribeProvider();
                ApplyProviderConfiguration();

                _lastError = null;
                _health =
                    new TrackingSourceHealth(
                        TrackingSourceHealthState.Starting,
                        0,
                        float.NaN,
                        null);
                _enabledAtUs =
                    MonotonicClock.NowMicroseconds();
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Ultraleap recovery failed: " +
                    exception.Message;
                SetFault(error);
                return false;
            }
        }

        public IReadOnlyList<TrackingRuntimeSetting>
            GetConfigurationSettings()
        {
            return new[]
            {
                new TrackingRuntimeSetting(
                    "tracking-mode",
                    "Tracking Mode",
                    trackingMode.ToString(),
                    "Desktop 또는 Screentop",
                    "책상 위 센서는 Desktop, 모니터 상단/하단 고정은 Screentop을 사용합니다."),
                new TrackingRuntimeSetting(
                    "service-ip",
                    "Tracking Service IP",
                    serviceIp,
                    "127.0.0.1",
                    "로컬 Tracking Service는 127.0.0.1을 사용합니다."),
                new TrackingRuntimeSetting(
                    "service-port",
                    "Tracking Service Port",
                    servicePort.ToString(
                        CultureInfo.InvariantCulture),
                    "12345",
                    "macOS에서도 포트를 명시해 연결하도록 기본 12345를 사용합니다."),
                new TrackingRuntimeSetting(
                    "offset-x",
                    "위치 X (m)",
                    localPositionOffset.x.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    "0"),
                new TrackingRuntimeSetting(
                    "offset-y",
                    "위치 Y (m)",
                    localPositionOffset.y.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    "0"),
                new TrackingRuntimeSetting(
                    "offset-z",
                    "위치 Z (m)",
                    localPositionOffset.z.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    "0"),
                new TrackingRuntimeSetting(
                    "rotation-x",
                    "회전 X (deg)",
                    localEulerOffset.x.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    "0"),
                new TrackingRuntimeSetting(
                    "rotation-y",
                    "회전 Y (deg)",
                    localEulerOffset.y.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    "0"),
                new TrackingRuntimeSetting(
                    "rotation-z",
                    "회전 Z (deg)",
                    localEulerOffset.z.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    "0")
            };
        }

        public bool TryApplyConfiguration(
            IReadOnlyDictionary<string, string> values,
            out string error)
        {
            error = null;

            if (values == null)
            {
                error =
                    "Ultraleap 설정 값이 없습니다.";
                return false;
            }

            values.TryGetValue(
                "tracking-mode",
                out var modeText);

            if (!Enum.TryParse(
                    modeText?.Trim(),
                    ignoreCase: true,
                    out LeapServiceProvider
                        .TrackingOptimizationMode nextMode) ||
                nextMode ==
                    LeapServiceProvider
                        .TrackingOptimizationMode.HMD)
            {
                error =
                    "Tracking Mode는 Desktop 또는 Screentop이어야 합니다.";
                return false;
            }

            values.TryGetValue(
                "service-ip",
                out var ipText);
            var nextIp =
                string.IsNullOrWhiteSpace(
                    ipText)
                    ? "127.0.0.1"
                    : ipText.Trim();

            if (!IPAddress.TryParse(
                    nextIp,
                    out var address) ||
                address.AddressFamily !=
                    AddressFamily.InterNetwork)
            {
                error =
                    "Tracking Service IP는 올바른 IPv4 주소여야 합니다.";
                return false;
            }

            if (!TryParseInt(
                    values,
                    "service-port",
                    servicePort,
                    1,
                    65535,
                    out var nextPort))
            {
                error =
                    "Tracking Service Port는 1~65535 범위여야 합니다.";
                return false;
            }

            if (!TryParseFloat(
                    values,
                    "offset-x",
                    localPositionOffset.x,
                    out var offsetX) ||
                !TryParseFloat(
                    values,
                    "offset-y",
                    localPositionOffset.y,
                    out var offsetY) ||
                !TryParseFloat(
                    values,
                    "offset-z",
                    localPositionOffset.z,
                    out var offsetZ) ||
                !TryParseFloat(
                    values,
                    "rotation-x",
                    localEulerOffset.x,
                    out var rotationX) ||
                !TryParseFloat(
                    values,
                    "rotation-y",
                    localEulerOffset.y,
                    out var rotationY) ||
                !TryParseFloat(
                    values,
                    "rotation-z",
                    localEulerOffset.z,
                    out var rotationZ))
            {
                error =
                    "Ultraleap 위치/회전 값은 유한한 숫자여야 합니다.";
                return false;
            }

            trackingMode = nextMode;
            serviceIp = nextIp;
            servicePort = nextPort;
            localPositionOffset =
                new Vector3(
                    offsetX,
                    offsetY,
                    offsetZ);
            localEulerOffset =
                new Vector3(
                    rotationX,
                    rotationY,
                    rotationZ);

            ApplyTrackingSpace();

            if (enabled &&
                Application.isPlaying)
            {
                try
                {
                    ApplyProviderConfiguration();
                }
                catch (Exception exception)
                {
                    error =
                        "Ultraleap 설정 적용 실패: " +
                        exception.Message;
                    SetFault(error);
                    return false;
                }
            }

            return true;
        }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            frame = _latestFrame;
            return frame != null;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot)
        {
            if ((region &
                 TrackingRegion.Hands) ==
                0)
            {
                snapshot = default;
                return false;
            }

            snapshot =
                new TrackingSourceHealthSnapshot(
                    SourceId,
                    TrackingSourceKind
                        .UltraleapHands,
                    TrackingRegion.Hands,
                    _health,
                    _lastFrameRuntimeUs);
            return true;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "tracking.ultraleap.enabled",
                    enabled ? 1.0 : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "tracking.ultraleap.health",
                    (int)_health.State,
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "tracking.ultraleap.hands",
                    _trackedHandCount,
                    "count"));

            if (_lastFrameRuntimeUs > 0)
            {
                output.Add(
                    new RuntimeMetric(
                        "tracking.ultraleap.frame_age",
                        TrackingTimestampMath
                            .AgeMillisecondsOrNaN(
                                MonotonicClock
                                    .NowMicroseconds(),
                                _lastFrameRuntimeUs),
                        "ms"));
            }
        }

        public void ConfigureProvider(
            LeapServiceProvider value)
        {
            if (ReferenceEquals(
                    provider,
                    value))
            {
                return;
            }

            UnsubscribeProvider();
            provider = value;
            ApplyTrackingSpace();

            if (enabled &&
                Application.isPlaying)
            {
                SubscribeProvider();
                ApplyProviderConfiguration();
            }
        }

        private void ResolveProvider()
        {
            provider ??=
                GetComponent<
                    LeapServiceProvider>();
        }

        private void SubscribeProvider()
        {
            ResolveProvider();

            if (provider == null ||
                _subscribed)
            {
                return;
            }

            provider.OnUpdateFrame +=
                HandleUltraleapFrame;
            _subscribed = true;
        }

        private void UnsubscribeProvider()
        {
            if (provider != null &&
                _subscribed)
            {
                provider.OnUpdateFrame -=
                    HandleUltraleapFrame;
            }

            _subscribed = false;
        }

        private void ApplyProviderConfiguration()
        {
            ResolveProvider();

            if (provider == null)
            {
                throw new InvalidOperationException(
                    "LeapServiceProvider is unavailable.");
            }

            provider.SetTargetServiceIPPortToConnectTo(
                serviceIp,
                servicePort.ToString(
                    CultureInfo.InvariantCulture));
            provider.ChangeTrackingMode(
                trackingMode);
        }

        private void ApplyTrackingSpace()
        {
            transform.localPosition =
                localPositionOffset;
            transform.localRotation =
                Quaternion.Euler(
                    localEulerOffset);
        }

        private void HandleUltraleapFrame(
            Frame frame)
        {
            if (!enabled ||
                frame == null)
            {
                return;
            }

            var nowUs =
                MonotonicClock
                    .NowMicroseconds();
            var normalizationRoot =
                transform.parent;

            NormalizedHandState left = null;
            NormalizedHandState right = null;
            var leftConfidence = -1f;
            var rightConfidence = -1f;

            if (frame.Hands != null)
            {
                for (var i = 0;
                     i < frame.Hands.Count;
                     i++)
                {
                    var hand =
                        frame.Hands[i];

                    if (hand == null)
                    {
                        continue;
                    }

                    var confidence =
                        SanitizeConfidence(
                            hand.Confidence);

                    if (hand.IsLeft)
                    {
                        if (left == null ||
                            confidence >
                                leftConfidence)
                        {
                            left =
                                ConvertHand(
                                    hand,
                                    normalizationRoot);
                            leftConfidence =
                                confidence;
                        }
                    }
                    else if (right == null ||
                             confidence >
                                 rightConfidence)
                    {
                        right =
                            ConvertHand(
                                hand,
                                normalizationRoot);
                        rightConfidence =
                            confidence;
                    }
                }
            }

            var regions =
                TrackingRegion.None;
            if (left != null)
            {
                regions |=
                    TrackingRegion.LeftHand;
            }
            if (right != null)
            {
                regions |=
                    TrackingRegion.RightHand;
            }

            _trackedHandCount =
                (left != null ? 1 : 0) +
                (right != null ? 1 : 0);

            var confidenceValue =
                AverageConfidence(
                    leftConfidence,
                    rightConfidence);

            _lastFrameRuntimeUs =
                nowUs;
            _latestFrame =
                new TrackingFrame(
                    ++_normalizedSequence,
                    sourceTimestampUs:
                        frame.Timestamp,
                    validRegions:
                        regions,
                    confidence:
                        confidenceValue,
                    subjectDetected:
                        regions !=
                            TrackingRegion.None,
                    leftHand:
                        left,
                    rightHand:
                        right,
                    sourceId:
                        SourceId,
                    runtimeTimestampUs:
                        nowUs);

            _health =
                new TrackingSourceHealth(
                    TrackingSourceHealthState.Healthy,
                    frame.Timestamp,
                    confidenceValue,
                    null);
            _lastError = null;

            UpdatePresence(
                nowUs,
                sourceAvailable: true,
                subjectEvidence:
                    regions !=
                    TrackingRegion.None);
        }

        private void UpdatePresence(
            long nowUs,
            bool sourceAvailable,
            bool subjectEvidence)
        {
            _presence =
                new TrackingPresenceSnapshot(
                    ++_presenceSequence,
                    nowUs,
                    !sourceAvailable
                        ? SubjectPresenceState.Unknown
                        : subjectEvidence
                            ? SubjectPresenceState.Present
                            : SubjectPresenceState.Absent,
                    faceSourceAvailable:
                        false,
                    bodyHandsSourceAvailable:
                        sourceAvailable,
                    fullBodySourceAvailable:
                        false,
                    faceSubjectEvidence:
                        false,
                    bodyHandsSubjectEvidence:
                        subjectEvidence,
                    fullBodySubjectEvidence:
                        false,
                    anySourceAvailable:
                        sourceAvailable,
                    subjectEvidence:
                        subjectEvidence,
                    events:
                        TrackingPresenceEvents.None);
        }

        private void SetFault(
            string error)
        {
            _lastError =
                string.IsNullOrWhiteSpace(
                    error)
                    ? "Ultraleap tracking failed."
                    : error;
            _health =
                new TrackingSourceHealth(
                    TrackingSourceHealthState.Faulted,
                    _lastFrameRuntimeUs,
                    float.NaN,
                    _lastError);
        }

        private static NormalizedHandState ConvertHand(
            Hand hand,
            Transform normalizationRoot)
        {
            if (hand == null)
            {
                return null;
            }

            var joints =
                new TrackingPoint[
                    (int)HandJoint.Count];
            var confidence =
                SanitizeConfidence(
                    hand.Confidence);

            if (!TryPoint(
                    hand.WristPosition,
                    normalizationRoot,
                    confidence,
                    out joints[
                        (int)HandJoint.Wrist]))
            {
                return null;
            }

            if (!FillThumb(
                    hand.Thumb,
                    normalizationRoot,
                    confidence,
                    joints) ||
                !FillFinger(
                    hand.Index,
                    HandJoint.IndexMcp,
                    HandJoint.IndexPip,
                    HandJoint.IndexDip,
                    HandJoint.IndexTip,
                    normalizationRoot,
                    confidence,
                    joints) ||
                !FillFinger(
                    hand.Middle,
                    HandJoint.MiddleMcp,
                    HandJoint.MiddlePip,
                    HandJoint.MiddleDip,
                    HandJoint.MiddleTip,
                    normalizationRoot,
                    confidence,
                    joints) ||
                !FillFinger(
                    hand.Ring,
                    HandJoint.RingMcp,
                    HandJoint.RingPip,
                    HandJoint.RingDip,
                    HandJoint.RingTip,
                    normalizationRoot,
                    confidence,
                    joints) ||
                !FillFinger(
                    hand.Pinky,
                    HandJoint.LittleMcp,
                    HandJoint.LittlePip,
                    HandJoint.LittleDip,
                    HandJoint.LittleTip,
                    normalizationRoot,
                    confidence,
                    joints))
            {
                return null;
            }

            return new NormalizedHandState(
                hand.IsLeft,
                joints,
                SnapshotArrayOwnership.Transfer);
        }

        private static bool FillThumb(
            Finger finger,
            Transform root,
            float confidence,
            TrackingPoint[] joints)
        {
            if (finger == null)
            {
                return false;
            }

            return
                TryPoint(
                    finger.Proximal.PrevJoint,
                    root,
                    confidence,
                    out joints[
                        (int)HandJoint.ThumbCmc]) &&
                TryPoint(
                    finger.Proximal.NextJoint,
                    root,
                    confidence,
                    out joints[
                        (int)HandJoint.ThumbMcp]) &&
                TryPoint(
                    finger.Intermediate.NextJoint,
                    root,
                    confidence,
                    out joints[
                        (int)HandJoint.ThumbIp]) &&
                TryPoint(
                    finger.Distal.NextJoint,
                    root,
                    confidence,
                    out joints[
                        (int)HandJoint.ThumbTip]);
        }

        private static bool FillFinger(
            Finger finger,
            HandJoint mcp,
            HandJoint pip,
            HandJoint dip,
            HandJoint tip,
            Transform root,
            float confidence,
            TrackingPoint[] joints)
        {
            if (finger == null)
            {
                return false;
            }

            return
                TryPoint(
                    finger.Proximal.PrevJoint,
                    root,
                    confidence,
                    out joints[(int)mcp]) &&
                TryPoint(
                    finger.Proximal.NextJoint,
                    root,
                    confidence,
                    out joints[(int)pip]) &&
                TryPoint(
                    finger.Intermediate.NextJoint,
                    root,
                    confidence,
                    out joints[(int)dip]) &&
                TryPoint(
                    finger.Distal.NextJoint,
                    root,
                    confidence,
                    out joints[(int)tip]);
        }

        private static bool TryPoint(
            Vector3 worldPosition,
            Transform root,
            float confidence,
            out TrackingPoint point)
        {
            point = default;

            if (!IsFinite(
                    worldPosition))
            {
                return false;
            }

            var position =
                root != null
                    ? root.InverseTransformPoint(
                        worldPosition)
                    : worldPosition;

            if (!IsFinite(
                    position))
            {
                return false;
            }

            point =
                new TrackingPoint(
                    new TrackingVector3(
                        position.x,
                        position.y,
                        position.z),
                    confidence);
            return true;
        }

        private static float AverageConfidence(
            float left,
            float right)
        {
            var leftValid =
                left >= 0f;
            var rightValid =
                right >= 0f;

            if (leftValid &&
                rightValid)
            {
                return
                    (left + right) *
                    0.5f;
            }

            if (leftValid)
            {
                return left;
            }

            if (rightValid)
            {
                return right;
            }

            return float.NaN;
        }

        private static float SanitizeConfidence(
            float value)
        {
            if (!float.IsFinite(
                    value))
            {
                return -1f;
            }

            return Mathf.Clamp01(
                value);
        }

        private static bool IsFinite(
            Vector3 value)
        {
            return
                float.IsFinite(
                    value.x) &&
                float.IsFinite(
                    value.y) &&
                float.IsFinite(
                    value.z);
        }

        private static bool TryParseInt(
            IReadOnlyDictionary<string, string> values,
            string key,
            int fallback,
            int minimum,
            int maximum,
            out int value)
        {
            value = fallback;

            if (!values.TryGetValue(
                    key,
                    out var text) ||
                string.IsNullOrWhiteSpace(
                    text))
            {
                return true;
            }

            return
                int.TryParse(
                    text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value) &&
                value >= minimum &&
                value <= maximum;
        }

        private static bool TryParseFloat(
            IReadOnlyDictionary<string, string> values,
            string key,
            float fallback,
            out float value)
        {
            value = fallback;

            if (!values.TryGetValue(
                    key,
                    out var text) ||
                string.IsNullOrWhiteSpace(
                    text))
            {
                return true;
            }

            return
                float.TryParse(
                    text.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) &&
                float.IsFinite(
                    value);
        }
    }
}
