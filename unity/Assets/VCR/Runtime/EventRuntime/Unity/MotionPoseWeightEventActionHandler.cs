using System;
using UnityEngine;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class MotionPoseWeightEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField]
        private string mixerId =
            "motion.primary";

        [SerializeField]
        private MotionExpressionMixer mixer;

        [SerializeField]
        private bool autoFindMixer = true;

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        private float _nextResolveTime;

        private void Awake()
        {
            ResolveMixer(
                force: true);
        }

        public void SetMixer(
            MotionExpressionMixer value,
            string id = "motion.primary")
        {
            mixer = value;

            if (!string.IsNullOrWhiteSpace(id))
            {
                mixerId = id;
            }
            _nextResolveTime = 0f;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (!string.Equals(
                    command.ActionType,
                    EventActionTypes.MotionPoseWeight,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ResolveMixer();

            if (mixer == null)
            {
                return false;
            }

            return
                string.IsNullOrWhiteSpace(
                    command.TargetId) ||
                string.Equals(
                    command.TargetId,
                    mixerId,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Motion pose-weight action target is unavailable or does not match.";
                return false;
            }

            if (!command.HasValue ||
                double.IsNaN(command.Value) ||
                double.IsInfinity(command.Value) ||
                command.Value < 0.0 ||
                command.Value > 1.0)
            {
                error =
                    "motion.pose_weight requires a finite value in the 0..1 range.";
                return false;
            }

            return mixer.TrySetPrimaryPoseLayerWeight(
                (float)command.Value,
                out error);
        }

        private void ResolveMixer(
            bool force = false)
        {
            if (mixer != null ||
                !autoFindMixer)
            {
                return;
            }

            var now =
                Time.unscaledTime;

            if (!force &&
                now < _nextResolveTime)
            {
                return;
            }

            _nextResolveTime =
                now +
                Mathf.Max(
                    0.1f,
                    autoFindRetrySeconds);

            mixer =
                FindFirstObjectByType<
                    MotionExpressionMixer>(
                    FindObjectsInactive.Exclude);

            if (mixer != null)
            {
                _nextResolveTime = 0f;
            }
        }
    }
}
