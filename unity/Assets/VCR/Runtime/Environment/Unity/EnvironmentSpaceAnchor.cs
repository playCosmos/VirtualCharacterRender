using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Environment.Unity
{
    /// <summary>
    /// Unity transform adapter for explicit environment coordinate spaces.
    ///
    /// Screen is intentionally an externally supplied Transform, typically a
    /// Canvas/overlay root. The adapter does not force screen-space content into
    /// a 3D camera transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentSpaceAnchor :
        MonoBehaviour,
        IEnvironmentSpaceTarget,
        IRuntimeMetricsSource
    {
        [SerializeField] private Transform contentRoot;
        [SerializeField] private Transform worldAnchor;
        [SerializeField] private Transform cameraAnchor;
        [SerializeField] private Transform screenAnchor;
        [SerializeField] private Transform characterAnchor;
        [SerializeField] private bool resetLocalTransformOnApply = true;

        private EnvironmentSpaceMode _currentMode =
            EnvironmentSpaceMode.World;
        private long _applyCount;
        private string _lastError;

        public EnvironmentSpaceMode CurrentMode =>
            _currentMode;

        public long ApplyCount =>
            _applyCount;

        public string LastError =>
            _lastError;

        private void Awake()
        {
            contentRoot ??=
                transform;
        }

        public void Configure(
            Transform content,
            Transform world,
            Transform camera,
            Transform screen,
            Transform character,
            bool resetLocalTransform = true)
        {
            contentRoot =
                content != null
                    ? content
                    : transform;
            worldAnchor = world;
            cameraAnchor = camera;
            screenAnchor = screen;
            characterAnchor = character;
            resetLocalTransformOnApply =
                resetLocalTransform;
        }

        public bool ValidateEnvironmentSpace(
            EnvironmentSpaceMode mode,
            out string error)
        {
            error = null;

            var content =
                contentRoot != null
                    ? contentRoot
                    : transform;

            var target =
                ResolveAnchor(mode);

            if (mode !=
                    EnvironmentSpaceMode.World &&
                target == null)
            {
                error =
                    $"Environment space '{mode}' requires an anchor.";
                _lastError = error;
                return false;
            }

            if (target != null &&
                (ReferenceEquals(
                     target,
                     content) ||
                 target.IsChildOf(
                     content)))
            {
                error =
                    $"Environment space '{mode}' would parent '{content.name}' to itself or one of its children.";
                _lastError = error;
                return false;
            }

            _lastError = null;
            return true;
        }

        public void ApplyEnvironmentSpace(
            EnvironmentSpaceMode mode)
        {
            if (!ValidateEnvironmentSpace(
                    mode,
                    out var error))
            {
                _lastError = error;
                return;
            }

            var content =
                contentRoot != null
                    ? contentRoot
                    : transform;
            var target =
                ResolveAnchor(mode);

            content.SetParent(
                target,
                worldPositionStays:
                    !resetLocalTransformOnApply);

            if (resetLocalTransformOnApply)
            {
                content.localPosition =
                    Vector3.zero;
                content.localRotation =
                    Quaternion.identity;
                content.localScale =
                    Vector3.one;
            }

            _currentMode = mode;
            _applyCount++;
            _lastError = null;
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
                    "environment.space.mode",
                    (int)_currentMode,
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "environment.space.applies",
                    _applyCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "environment.space.error",
                    string.IsNullOrEmpty(
                        _lastError)
                        ? 0.0
                        : 1.0,
                    "bool"));
        }

        private Transform ResolveAnchor(
            EnvironmentSpaceMode mode)
        {
            return mode switch
            {
                EnvironmentSpaceMode.World =>
                    worldAnchor,
                EnvironmentSpaceMode.Camera =>
                    cameraAnchor,
                EnvironmentSpaceMode.Screen =>
                    screenAnchor,
                EnvironmentSpaceMode.Character =>
                    characterAnchor,
                _ => null
            };
        }
    }
}
