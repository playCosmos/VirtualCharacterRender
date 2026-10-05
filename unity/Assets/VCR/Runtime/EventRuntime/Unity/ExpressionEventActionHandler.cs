using System;
using UnityEngine;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class ExpressionEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField]
        private string layerId =
            "expression.event";

        [SerializeField]
        private ManualExpressionLayerSource source;

        [SerializeField]
        private bool autoFindSource = true;

        private void Awake()
        {
            ResolveSource();
        }

        public void SetExpressionSource(
            ManualExpressionLayerSource value,
            string id = "expression.event")
        {
            source = value;

            if (!string.IsNullOrWhiteSpace(
                    id))
            {
                layerId = id;
            }
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (!string.Equals(
                    command.ActionType,
                    EventActionTypes.ExpressionSet,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ResolveSource();

            if (source == null)
            {
                return false;
            }

            return
                string.IsNullOrWhiteSpace(
                    command.TargetId) ||
                string.Equals(
                    command.TargetId,
                    layerId,
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
                    "Expression action target is unavailable or does not match.";
                return false;
            }

            if (!StandardExpressionNames
                    .TryParse(
                        command.Name,
                        out var expression))
            {
                error =
                    $"Unknown standard expression '{command.Name}'.";
                return false;
            }

            if (!command.HasValue ||
                double.IsNaN(command.Value) ||
                double.IsInfinity(command.Value) ||
                command.Value < 0.0 ||
                command.Value > 1.0)
            {
                error =
                    "expression.set requires a finite value in the 0..1 range.";
                return false;
            }

            if (!source.SetExpression(
                    expression,
                    (float)command.Value))
            {
                error =
                    "Expression layer rejected the requested value.";
                return false;
            }

            return true;
        }

        private void ResolveSource()
        {
            if (source != null ||
                !autoFindSource)
            {
                return;
            }

            source =
                FindFirstObjectByType<
                    ManualExpressionLayerSource>(
                    FindObjectsInactive.Exclude);
        }
    }
}
