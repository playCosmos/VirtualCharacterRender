using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Environment;
using VCR.Runtime.Environment.Unity;

namespace VCR.Editor.P6
{
    public static class P6EnvironmentRuntimeValidation
    {
        [MenuItem("VCR/P6/Validate Environment Runtime")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures = new List<string>();

            ValidateTransitionSpecSanitization(
                failures);
            ValidateScheduler(failures);
            ValidateRuntime(failures);
            ValidateDestroyedTargetLifetime(failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P6 environment runtime validation: PASS " +
                    "(state roots, space anchors, Cut/Fade/Crossfade transitions, 2D image/video/parallax targets, lighting influence, dispatch-cost diagnostics, atomic guards, event/manual/scheduled dispatch, failure isolation, drop-only scheduler)");
                return true;
            }

            Debug.LogError(
                "VCR P6 environment runtime validation: FAIL\n" +
                string.Join("\n", failures));
            return false;
        }

        private static void ValidateTransitionSpecSanitization(
            List<string> failures)
        {
            var nanDuration =
                new EnvironmentTransitionSpec(
                    EnvironmentTransitionMode.Fade,
                    float.NaN);
            var infiniteDuration =
                new EnvironmentTransitionSpec(
                    EnvironmentTransitionMode.Crossfade,
                    float.PositiveInfinity);
            var invalidMode =
                new EnvironmentTransitionSpec(
                    (EnvironmentTransitionMode)999,
                    1f);

            Expect(
                nanDuration.Mode ==
                    EnvironmentTransitionMode.Fade &&
                Math.Abs(
                    nanDuration.DurationSeconds) <
                    0.0001f &&
                nanDuration.IsImmediate &&
                infiniteDuration.Mode ==
                    EnvironmentTransitionMode.Crossfade &&
                Math.Abs(
                    infiniteDuration.DurationSeconds) <
                    0.0001f &&
                infiniteDuration.IsImmediate &&
                invalidMode.Mode ==
                    EnvironmentTransitionMode.Cut &&
                invalidMode.IsImmediate,
                "environment transition specs must sanitize non-finite durations and unsupported modes into safe immediate transitions",
                failures);

            var nonFiniteLighting =
                new EnvironmentLightingProfile(
                    float.NaN,
                    float.PositiveInfinity,
                    float.NegativeInfinity,
                    float.NaN,
                    float.PositiveInfinity);
            var clampedLighting =
                new EnvironmentLightingProfile(
                    -1f,
                    2f,
                    0.5f,
                    -2f,
                    2f);

            Expect(
                Math.Abs(
                    nonFiniteLighting.Red -
                    1f) <
                    0.0001f &&
                Math.Abs(
                    nonFiniteLighting.Green -
                    1f) <
                    0.0001f &&
                Math.Abs(
                    nonFiniteLighting.Blue -
                    1f) <
                    0.0001f &&
                Math.Abs(
                    nonFiniteLighting.IntensityMultiplier -
                    1f) <
                    0.0001f &&
                Math.Abs(
                    nonFiniteLighting.Weight) <
                    0.0001f &&
                Math.Abs(
                    clampedLighting.Red) <
                    0.0001f &&
                Math.Abs(
                    clampedLighting.Green -
                    1f) <
                    0.0001f &&
                Math.Abs(
                    clampedLighting.Blue -
                    0.5f) <
                    0.0001f &&
                Math.Abs(
                    clampedLighting.IntensityMultiplier) <
                    0.0001f &&
                Math.Abs(
                    clampedLighting.Weight -
                    1f) <
                    0.0001f,
                "environment lighting profiles must sanitize non-finite values to neutral/no-influence fallbacks while preserving finite clamp semantics",
                failures);
        }

        private static void ValidateScheduler(
            List<string> failures)
        {
            var scheduler =
                new EnvironmentUpdateScheduler();

            scheduler.Configure(
                EnvironmentUpdatePolicy.Static,
                nowUs: 0);

            Expect(
                !scheduler.HasRecurringUpdates &&
                !scheduler.IsDue(1_000_000),
                "Static policy must never schedule recurring updates",
                failures);

            scheduler.Configure(
                EnvironmentUpdatePolicy.EventDriven,
                nowUs: 0);

            Expect(
                !scheduler.HasRecurringUpdates &&
                !scheduler.IsDue(1_000_000),
                "EventDriven policy must never schedule recurring updates",
                failures);

            scheduler.Configure(
                EnvironmentUpdatePolicy.Hz10,
                nowUs: 1_000_000);

            Expect(
                scheduler.IsDue(1_000_000),
                "Hz10 must allow its first scheduled update immediately",
                failures);

            scheduler.MarkDispatched(
                1_000_000);

            Expect(
                !scheduler.IsDue(1_099_999) &&
                scheduler.IsDue(1_100_000),
                "Hz10 interval must be 100ms",
                failures);

            scheduler.MarkDispatched(
                1_500_000);

            Expect(
                !scheduler.IsDue(1_500_000) &&
                !scheduler.IsDue(1_599_999) &&
                scheduler.IsDue(1_600_000),
                "stalled Hz10 updates must schedule from the latest dispatch instead of burst-catching up",
                failures);

            scheduler.Configure(
                EnvironmentUpdatePolicy.EveryFrame,
                nowUs: 0);

            Expect(
                scheduler.IsDue(0) &&
                scheduler.IsDue(1),
                "EveryFrame policy must be due whenever the driver runs",
                failures);
        }

        private static void ValidateRuntime(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P6 Environment Runtime Validation");

                var day =
                    new GameObject("Day");
                var night =
                    new GameObject("Night");

                day.transform.SetParent(
                    root.transform,
                    false);
                night.transform.SetParent(
                    root.transform,
                    false);

                var runtime =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();
                var target =
                    root.AddComponent<
                        P6FakeEnvironmentUpdateTarget>();

                var worldAnchor =
                    new GameObject("World Anchor");
                var cameraAnchor =
                    new GameObject("Camera Anchor");
                var screenAnchor =
                    new GameObject("Screen Anchor");
                var characterAnchor =
                    new GameObject("Character Anchor");
                var spaceContent =
                    new GameObject("Space Content");

                worldAnchor.transform.SetParent(
                    root.transform,
                    false);
                cameraAnchor.transform.SetParent(
                    root.transform,
                    false);
                screenAnchor.transform.SetParent(
                    root.transform,
                    false);
                characterAnchor.transform.SetParent(
                    root.transform,
                    false);
                spaceContent.transform.SetParent(
                    worldAnchor.transform,
                    false);

                var dayCanvas =
                    day.AddComponent<CanvasGroup>();
                var nightCanvas =
                    night.AddComponent<CanvasGroup>();

                var dayCanvasBinding =
                    new EnvironmentCanvasGroupBinding();
                dayCanvasBinding.Configure(
                    "day",
                    dayCanvas);

                var nightCanvasBinding =
                    new EnvironmentCanvasGroupBinding();
                nightCanvasBinding.Configure(
                    "night",
                    nightCanvas);

                var canvasTransition =
                    root.AddComponent<
                        CanvasGroupEnvironmentTransitionTarget>();
                canvasTransition.Configure(
                    dayCanvasBinding,
                    nightCanvasBinding);

                Expect(
                    canvasTransition
                        .ValidateEnvironmentTransition(
                            new EnvironmentTransitionSpec(
                                EnvironmentTransitionMode.Crossfade,
                                0.5f),
                            "day",
                            "night",
                            out var canvasTransitionError) &&
                    string.IsNullOrEmpty(
                        canvasTransitionError),
                    "CanvasGroup transition target must validate a complete Crossfade binding",
                    failures);

                canvasTransition.ApplyEnvironmentTransition(
                    new EnvironmentTransitionContext(
                        1,
                        0,
                        "day",
                        "night",
                        EnvironmentTransitionMode.Crossfade,
                        0.25f,
                        0f));

                Expect(
                    Math.Abs(
                        dayCanvas.alpha - 0.75f) <
                        0.001f &&
                    Math.Abs(
                        nightCanvas.alpha - 0.25f) <
                        0.001f,
                    "CanvasGroup Crossfade must apply complementary outgoing/incoming alpha",
                    failures);

                canvasTransition.ApplyEnvironmentTransition(
                    new EnvironmentTransitionContext(
                        3,
                        0,
                        "day",
                        "night",
                        EnvironmentTransitionMode.Fade,
                        0.25f,
                        0f));

                Expect(
                    Math.Abs(
                        dayCanvas.alpha - 0.5f) <
                        0.001f &&
                    Math.Abs(
                        nightCanvas.alpha) <
                        0.001f,
                    "CanvasGroup Fade first half must fade out the previous state before revealing the next state",
                    failures);

                canvasTransition.ApplyEnvironmentTransition(
                    new EnvironmentTransitionContext(
                        4,
                        0,
                        "day",
                        "night",
                        EnvironmentTransitionMode.Fade,
                        0.75f,
                        0f));

                Expect(
                    Math.Abs(
                        dayCanvas.alpha) <
                        0.001f &&
                    Math.Abs(
                        nightCanvas.alpha - 0.5f) <
                        0.001f,
                    "CanvasGroup Fade second half must fade in the next state after the previous state is hidden",
                    failures);

                canvasTransition.ApplyEnvironmentTransition(
                    new EnvironmentTransitionContext(
                        2,
                        0,
                        "night",
                        "day",
                        EnvironmentTransitionMode.Cut,
                        1f,
                        0f));

                Expect(
                    Math.Abs(
                        dayCanvas.alpha - 1f) <
                        0.001f &&
                    Math.Abs(
                        nightCanvas.alpha) <
                        0.001f,
                    "Cut notification must restore the active CanvasGroup alpha and clear inactive state alpha",
                    failures);

                Expect(
                    !canvasTransition
                        .ValidateEnvironmentTransition(
                            new EnvironmentTransitionSpec(
                                EnvironmentTransitionMode.Dissolve,
                                0.5f),
                            "day",
                            "night",
                            out var dissolveError) &&
                    !string.IsNullOrEmpty(
                        dissolveError),
                    "CanvasGroup transition target must explicitly reject shader-driven Dissolve",
                    failures);

                var parallaxObject =
                    new GameObject(
                        "Parallax Layer",
                        typeof(RectTransform));
                parallaxObject.transform.SetParent(
                    root.transform,
                    false);

                var parallaxReference =
                    new GameObject(
                        "Parallax Reference");
                parallaxReference.transform.SetParent(
                    root.transform,
                    false);

                var parallaxTransform =
                    parallaxObject.GetComponent<
                        RectTransform>();
                parallaxTransform.anchoredPosition =
                    new Vector2(
                        5f,
                        6f);

                parallaxReference.transform.localPosition =
                    new Vector3(
                        1f,
                        2f,
                        0f);

                var parallaxTarget =
                    parallaxObject.AddComponent<
                        Environment2DLayerTarget>();

                parallaxTarget.ConfigureParallax(
                    null,
                    parallaxTransform,
                    parallaxReference.transform,
                    new Vector2(
                        10f,
                        20f));

                Expect(
                    parallaxTarget.ValidateConfiguration(
                        out var parallaxError) &&
                    string.IsNullOrEmpty(
                        parallaxError),
                    "Parallax 2D layer must validate with an explicit RectTransform and reference Transform",
                    failures);

                parallaxReference.transform.localPosition =
                    new Vector3(
                        3f,
                        1f,
                        0f);

                parallaxTarget.UpdateEnvironment(
                    new EnvironmentUpdateContext(
                        1,
                        0,
                        0.1f,
                        EnvironmentUpdateReason.Scheduled,
                        "day"));

                Expect(
                    parallaxTransform.anchoredPosition ==
                        new Vector2(
                            25f,
                            -14f) &&
                    parallaxTarget.ParallaxUpdateCount == 1,
                    "Parallax layer must move only when an environment update is dispatched and apply configured XY scale",
                    failures);

                Expect(
                    typeof(Environment2DLayerTarget)
                        .GetMethod(
                            "Update",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic |
                            BindingFlags.Public) ==
                    null,
                    "2D environment layer target must not own a recurring Update loop",
                    failures);

                var invalidStaticLayer =
                    root.AddComponent<
                        Environment2DLayerTarget>();
                invalidStaticLayer.ConfigureStaticImage(
                    null);

                Expect(
                    !invalidStaticLayer
                        .ValidateConfiguration(
                            out var invalidStaticError) &&
                    !string.IsNullOrEmpty(
                        invalidStaticError),
                    "StaticImage layer must reject missing RawImage configuration",
                    failures);

                var videoObject =
                    new GameObject(
                        "Video Layer");
                videoObject.transform.SetParent(
                    root.transform,
                    false);

                var videoPlayer =
                    videoObject.AddComponent<
                        UnityEngine.Video.VideoPlayer>();
                var videoLayer =
                    videoObject.AddComponent<
                        Environment2DLayerTarget>();
                videoLayer.ConfigureVideo(
                    videoPlayer,
                    target: null,
                    autoPlay: false);

                Expect(
                    videoLayer.ValidateConfiguration(
                        out var videoError) &&
                    string.IsNullOrEmpty(
                        videoError) &&
                    videoLayer.Mode ==
                        Environment2DLayerMode.Video,
                    "Video 2D layer must validate a preconfigured VideoPlayer without requiring its own Update loop",
                    failures);

                var parallaxMetrics =
                    new List<RuntimeMetric>();
                parallaxTarget.CollectMetrics(
                    parallaxMetrics);

                Expect(
                    TryGetMetric(
                        parallaxMetrics,
                        "environment.2d.parallax_updates",
                        out var parallaxUpdates) &&
                    Math.Abs(
                        parallaxUpdates - 1.0) <
                    0.001,
                    "2D layer diagnostics must expose parallax update count",
                    failures);

                var spaceTarget =
                    spaceContent.AddComponent<
                        EnvironmentSpaceAnchor>();

                spaceTarget.Configure(
                    spaceContent.transform,
                    worldAnchor.transform,
                    cameraAnchor.transform,
                    screenAnchor.transform,
                    characterAnchor.transform,
                    resetLocalTransform: true);

                runtime.Configure(
                    "environment.p6.test",
                    "day",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                var lightingObject =
                    new GameObject(
                        "Environment Light");
                lightingObject.transform.SetParent(
                    root.transform,
                    false);

                var light =
                    lightingObject.AddComponent<Light>();
                light.type =
                    LightType.Directional;
                light.color =
                    Color.white;
                light.intensity =
                    2f;

                var lightingTarget =
                    lightingObject.AddComponent<
                        EnvironmentLightInfluenceTarget>();
                lightingTarget.Configure(
                    light);

                runtime.SetLightingTargets(
                    lightingTarget,
                    lightingTarget);

                var lightingProfile =
                    new EnvironmentLightingProfile(
                        red: 0f,
                        green: 0.5f,
                        blue: 1f,
                        intensityMultiplier: 2f,
                        weight: 0.5f);

                Expect(
                    runtime.SetLightingProfile(
                        lightingProfile,
                        out var lightingError) &&
                    string.IsNullOrEmpty(
                        lightingError) &&
                    runtime.LightingTargetCount == 1 &&
                    Mathf.Abs(
                        light.color.r - 0.5f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.g - 0.75f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.b - 1f) <
                        0.001f &&
                    Mathf.Abs(
                        light.intensity - 3f) <
                        0.001f,
                    "environment lighting profile must apply weighted color/intensity without duplicating targets",
                    failures);

                var invalidLightingObject =
                    new GameObject(
                        "Invalid Environment Light Target");
                invalidLightingObject.transform.SetParent(
                    root.transform,
                    false);

                var invalidLightingTarget =
                    invalidLightingObject.AddComponent<
                        EnvironmentLightInfluenceTarget>();

                var invalidLightingAccepted =
                    runtime.ConfigureLightingTargets(
                        new MonoBehaviour[]
                        {
                            invalidLightingTarget
                        },
                        out var invalidLightingError);

                Expect(
                    !invalidLightingAccepted &&
                    !string.IsNullOrEmpty(
                        invalidLightingError) &&
                    runtime.LightingTargetCount == 1 &&
                    Mathf.Abs(
                        light.intensity - 3f) <
                        0.001f,
                    "invalid lighting target must be rejected atomically without replacing the active valid target",
                    failures);

                var throwingLightingObject =
                    new GameObject(
                        "Throwing Environment Light Target");
                throwingLightingObject.transform.SetParent(
                    root.transform,
                    false);
                var throwingLightingTarget =
                    throwingLightingObject.AddComponent<
                        P6ConditionalThrowEnvironmentLightingTarget>();
                throwingLightingTarget.ThrowOnApply =
                    true;

                var throwingTargetAccepted =
                    runtime.ConfigureLightingTargets(
                        new MonoBehaviour[]
                        {
                            throwingLightingTarget
                        },
                        out var throwingTargetError);

                Expect(
                    !throwingTargetAccepted &&
                    !string.IsNullOrWhiteSpace(
                        throwingTargetError) &&
                    runtime.LightingTargetCount == 1 &&
                    Mathf.Abs(
                        light.color.r - 0.5f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.g - 0.75f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.b - 1f) <
                        0.001f &&
                    Mathf.Abs(
                        light.intensity - 3f) <
                        0.001f,
                    "lighting target apply failure must reject the new target set and restore the previous target/profile",
                    failures);

                throwingLightingTarget.ThrowOnApply =
                    false;
                throwingLightingTarget.ThrowOnValidate =
                    true;

                var throwingValidationAccepted =
                    runtime.ConfigureLightingTargets(
                        new MonoBehaviour[]
                        {
                            throwingLightingTarget
                        },
                        out var throwingValidationError);

                Expect(
                    !throwingValidationAccepted &&
                    !string.IsNullOrWhiteSpace(
                        throwingValidationError) &&
                    runtime.LightingTargetCount == 1 &&
                    Mathf.Abs(
                        light.intensity - 3f) <
                        0.001f,
                    "lighting target validation exceptions must be returned through false/error without replacing the active target set",
                    failures);

                throwingLightingTarget.ThrowOnValidate =
                    false;

                Expect(
                    runtime.ConfigureLightingTargets(
                        new MonoBehaviour[]
                        {
                            lightingTarget,
                            throwingLightingTarget
                        },
                        out var mixedLightingError) &&
                    string.IsNullOrWhiteSpace(
                        mixedLightingError) &&
                    runtime.LightingTargetCount == 2,
                    "lighting failure validation must configure a mixed healthy/throw-capable target set",
                    failures);

                throwingLightingTarget.ThrowOnApply =
                    true;

                var failedLightingProfile =
                    new EnvironmentLightingProfile(
                        red: 1f,
                        green: 0f,
                        blue: 0f,
                        intensityMultiplier: 0.5f,
                        weight: 1f);

                var failedProfileAccepted =
                    runtime.SetLightingProfile(
                        failedLightingProfile,
                        out var failedProfileError);

                Expect(
                    !failedProfileAccepted &&
                    !string.IsNullOrWhiteSpace(
                        failedProfileError) &&
                    Mathf.Abs(
                        runtime.LightingProfile.Red -
                        lightingProfile.Red) <
                        0.001f &&
                    Mathf.Abs(
                        runtime.LightingProfile.Green -
                        lightingProfile.Green) <
                        0.001f &&
                    Mathf.Abs(
                        runtime.LightingProfile.Blue -
                        lightingProfile.Blue) <
                        0.001f &&
                    Mathf.Abs(
                        runtime.LightingProfile.IntensityMultiplier -
                        lightingProfile.IntensityMultiplier) <
                        0.001f &&
                    Mathf.Abs(
                        runtime.LightingProfile.Weight -
                        lightingProfile.Weight) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.r - 0.5f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.g - 0.75f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.b - 1f) <
                        0.001f &&
                    Mathf.Abs(
                        light.intensity - 3f) <
                        0.001f,
                    "lighting profile apply failure must rollback the previous profile and already-mutated healthy targets",
                    failures);

                throwingLightingTarget.ThrowOnApply =
                    false;
                runtime.SetLightingTargets(
                    lightingTarget);

                runtime.SetLightingTargets();

                Expect(
                    runtime.LightingTargetCount == 0 &&
                    Mathf.Abs(
                        light.color.r - 1f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.g - 1f) <
                        0.001f &&
                    Mathf.Abs(
                        light.color.b - 1f) <
                        0.001f &&
                    Mathf.Abs(
                        light.intensity - 2f) <
                        0.001f,
                    "removing environment lighting targets must restore their source Light values",
                    failures);

                runtime.SetLightingTargets(
                    lightingTarget);
                runtime.SetLightingProfile(
                    lightingProfile,
                    out _);

                runtime.SetSpaceTargets(
                    spaceTarget,
                    spaceTarget);

                Expect(
                    runtime.SpaceTargetCount == 1 &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        worldAnchor.transform),
                    "space targets must be de-duplicated and apply the current World anchor",
                    failures);

                spaceContent.transform.localPosition =
                    new Vector3(
                        3f,
                        4f,
                        5f);

                var cameraApplied =
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Camera,
                        out var spaceError);

                Expect(
                    cameraApplied &&
                    string.IsNullOrEmpty(
                        spaceError) &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.Camera &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        cameraAnchor.transform) &&
                    spaceContent.transform.localPosition ==
                        Vector3.zero,
                    "Camera space must reparent to the camera anchor and reset local transform",
                    failures);

                spaceTarget.Configure(
                    spaceContent.transform,
                    worldAnchor.transform,
                    cameraAnchor.transform,
                    spaceContent.transform,
                    characterAnchor.transform,
                    resetLocalTransform: true);

                var invalidScreen =
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Screen,
                        out var invalidScreenError);

                Expect(
                    !invalidScreen &&
                    !string.IsNullOrEmpty(
                        invalidScreenError) &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.Camera &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        cameraAnchor.transform),
                    "invalid Screen anchor must fail validation without partially moving content or changing the active space mode",
                    failures);

                spaceTarget.Configure(
                    spaceContent.transform,
                    worldAnchor.transform,
                    cameraAnchor.transform,
                    screenAnchor.transform,
                    characterAnchor.transform,
                    resetLocalTransform: true);

                Expect(
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Screen,
                        out _) &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        screenAnchor.transform),
                    "Screen space must use the explicit screen/Canvas anchor",
                    failures);

                Expect(
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Character,
                        out _) &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        characterAnchor.transform),
                    "Character space must follow the explicit character anchor",
                    failures);

                var throwingSpaceTarget =
                    root.AddComponent<
                        P6ConditionalThrowEnvironmentSpaceTarget>();

                Expect(
                    runtime.ConfigureSpaceTargets(
                        new MonoBehaviour[]
                        {
                            spaceTarget,
                            throwingSpaceTarget
                        },
                        out var throwingSpaceConfigError) &&
                    string.IsNullOrEmpty(
                        throwingSpaceConfigError),
                    "space target exception validation setup must configure while current Character mode is safe",
                    failures);

                Expect(
                    !runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Camera,
                        out var throwingSpaceError) &&
                    !string.IsNullOrEmpty(
                        throwingSpaceError) &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.Character &&
                    ReferenceEquals(
                        spaceContent.transform.parent,
                        characterAnchor.transform),
                    "space target apply exceptions must fail closed and roll previously applied targets back to the prior mode",
                    failures);

                runtime.SetSpaceTargets(
                    spaceTarget);

                var dayBinding =
                    new EnvironmentStateBinding();
                dayBinding.Configure(
                    "day",
                    day);

                var nightBinding =
                    new EnvironmentStateBinding();
                nightBinding.Configure(
                    "night",
                    night);

                var configured =
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            dayBinding,
                            nightBinding
                        },
                        out var bindingError);

                runtime.SetUpdateTargets(
                    target);

                Expect(
                    configured &&
                    string.IsNullOrEmpty(
                        bindingError),
                    "valid environment state bindings must configure",
                    failures);

                Expect(
                    day.activeSelf &&
                    !night.activeSelf,
                    "configuring bindings must apply the current state root immediately",
                    failures);

                dayBinding.Configure(
                    "external-day",
                    night);
                nightBinding.Configure(
                    "external-night",
                    day);

                var stateEvents = 0;
                Action<EnvironmentStateChange>
                    throwingStateSubscriber =
                        _ =>
                            throw new InvalidOperationException(
                                "environment state subscriber failure");

                runtime.StateChanged +=
                    throwingStateSubscriber;
                runtime.StateChanged += _ =>
                {
                    stateEvents++;
                };

                var changed = false;
                string stateError = null;

                try
                {
                    changed =
                        runtime.SetState(
                            "night",
                            out stateError);
                }
                catch (Exception exception)
                {
                    stateError =
                        "subscriber isolation failure: " +
                        exception.Message;
                }

                runtime.StateChanged -=
                    throwingStateSubscriber;

                dayBinding.Configure(
                    "day",
                    day);
                nightBinding.Configure(
                    "night",
                    night);

                Expect(
                    changed &&
                    string.IsNullOrEmpty(
                        stateError) &&
                    !day.activeSelf &&
                    night.activeSelf,
                    "SetState must atomically switch bound roots and remain isolated from external mutation of the original binding objects",
                    failures);

                Expect(
                    stateEvents == 1 &&
                    target.UpdateCount == 1 &&
                    target.LastContext.Reason ==
                        EnvironmentUpdateReason.StateChanged &&
                    target.LastContext.StateId ==
                        "night",
                    "state change must emit one state event and one environment update dispatch",
                    failures);

                runtime.SetState(
                    "night",
                    out _);

                Expect(
                    stateEvents == 1 &&
                    target.UpdateCount == 1,
                    "repeating the active state must be idempotent",
                    failures);

                var unknown =
                    runtime.SetState(
                        "missing",
                        out var missingError);

                Expect(
                    !unknown &&
                    !string.IsNullOrEmpty(
                        missingError) &&
                    !day.activeSelf &&
                    night.activeSelf &&
                    target.UpdateCount == 1,
                    "unknown states must fail without partially toggling roots or dispatching updates",
                    failures);

                var unsupportedTransition =
                    runtime.SetState(
                        "day",
                        new EnvironmentTransitionSpec(
                            EnvironmentTransitionMode.Crossfade,
                            0.5f),
                        out var unsupportedTransitionError);

                Expect(
                    !unsupportedTransition &&
                    !string.IsNullOrEmpty(
                        unsupportedTransitionError) &&
                    !day.activeSelf &&
                    night.activeSelf,
                    "non-Cut transition must be rejected when no transition target is configured",
                    failures);

                var transitionTarget =
                    root.AddComponent<
                        P6FakeEnvironmentTransitionTarget>();

                runtime.SetTransitionTargets(
                    transitionTarget,
                    transitionTarget);

                transitionTarget.ThrowOnValidate =
                    true;

                var transitionValidationAccepted =
                    runtime.SetState(
                        "day",
                        new EnvironmentTransitionSpec(
                            EnvironmentTransitionMode.Crossfade,
                            0.5f),
                        out var transitionValidationError);

                Expect(
                    !transitionValidationAccepted &&
                    !string.IsNullOrWhiteSpace(
                        transitionValidationError) &&
                    runtime.Status.StateId ==
                        "night" &&
                    !runtime.TransitionStatus.Active &&
                    !day.activeSelf &&
                    night.activeSelf,
                    "transition target validation exceptions must return false/error without changing state or starting a transition",
                    failures);

                transitionTarget.ThrowOnValidate =
                    false;

                var transitionStarted =
                    runtime.SetState(
                        "day",
                        new EnvironmentTransitionSpec(
                            EnvironmentTransitionMode.Crossfade,
                            0.5f),
                        out var transitionError);

                Expect(
                    transitionStarted &&
                    string.IsNullOrEmpty(
                        transitionError) &&
                    runtime.TransitionStatus.Active &&
                    runtime.TransitionTargetCount == 1 &&
                    day.activeSelf &&
                    night.activeSelf &&
                    transitionTarget.ApplyCount == 1 &&
                    Math.Abs(
                        transitionTarget
                            .LastContext.Progress) <
                    0.001f,
                    "Crossfade must keep both roots active, de-duplicate targets, and dispatch progress 0",
                    failures);

                var transitionStartUs =
                    runtime.TransitionStatus
                        .StartedAtTimestampUs;

                Expect(
                    runtime.TickTransition(
                        transitionStartUs +
                        250_000) &&
                    runtime.TransitionStatus.Active &&
                    Math.Abs(
                        runtime.TransitionStatus.Progress -
                        0.5f) <
                    0.01f &&
                    day.activeSelf &&
                    night.activeSelf &&
                    Math.Abs(
                        transitionTarget
                            .LastContext.Progress -
                        0.5f) <
                    0.01f,
                    "Crossfade midpoint must preserve both roots and dispatch progress 0.5",
                    failures);

                Expect(
                    runtime.TickTransition(
                        transitionStartUs +
                        500_000) &&
                    !runtime.TransitionStatus.Active &&
                    day.activeSelf &&
                    !night.activeSelf &&
                    Math.Abs(
                        transitionTarget
                            .LastContext.Progress -
                        1f) <
                    0.001f,
                    "Crossfade completion must keep only the new root active and dispatch progress 1",
                    failures);

                Expect(
                    runtime.RequestManualUpdate() &&
                    target.UpdateCount == 2 &&
                    target.LastContext.Reason ==
                        EnvironmentUpdateReason.Manual,
                    "manual environment updates must dispatch explicitly without requiring a recurring driver",
                    failures);

                Expect(
                    root.GetComponent<
                        EnvironmentUpdateDriver>() ==
                    null,
                    "EventDriven environment must not allocate a recurring Update driver",
                    failures);

                runtime.Configure(
                    "environment.p6.test",
                    "night",
                    EnvironmentUpdatePolicy.Hz10,
                    EnvironmentSpaceMode.World);

                var driver =
                    root.GetComponent<
                        EnvironmentUpdateDriver>();

                Expect(
                    driver != null &&
                    driver.enabled &&
                    runtime.RecurringUpdatesActive,
                    "Hz10 policy must lazily create and enable the recurring update driver",
                    failures);

                var firstDue =
                    MonotonicClock
                        .NowMicroseconds();

                Expect(
                    runtime.TickScheduled(
                        firstDue),
                    "Hz10 runtime must dispatch the first due scheduled update",
                    failures);

                Expect(
                    !runtime.TickScheduled(
                        firstDue + 50_000),
                    "Hz10 runtime must reject early scheduled ticks",
                    failures);

                Expect(
                    runtime.TickScheduled(
                        firstDue + 100_000),
                    "Hz10 runtime must dispatch the next scheduled update after 100ms",
                    failures);

                Expect(
                    runtime.ScheduledDispatchCount == 2 &&
                    target.LastContext.Reason ==
                        EnvironmentUpdateReason.Scheduled,
                    "scheduled dispatch count must include only due ticks",
                    failures);

                runtime.Configure(
                    "environment.p6.test",
                    "night",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                Expect(
                    driver != null &&
                    !driver.enabled &&
                    !runtime.RecurringUpdatesActive,
                    "returning to EventDriven must disable the recurring update driver",
                    failures);

                var duplicate =
                    new EnvironmentStateBinding();
                duplicate.Configure(
                    "night",
                    day);

                var duplicateAccepted =
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            nightBinding,
                            duplicate
                        },
                        out var duplicateError);

                Expect(
                    !duplicateAccepted &&
                    !string.IsNullOrEmpty(
                        duplicateError) &&
                    night.activeSelf,
                    "duplicate binding ids must be rejected without replacing the active binding set",
                    failures);

                var unsafeBinding =
                    new EnvironmentStateBinding();
                unsafeBinding.Configure(
                    "night",
                    root);

                var unsafeAccepted =
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            unsafeBinding
                        },
                        out var unsafeError);

                Expect(
                    !unsafeAccepted &&
                    !string.IsNullOrEmpty(
                        unsafeError),
                    "state roots that contain the runtime itself must be rejected",
                    failures);

                var throwingTarget =
                    root.AddComponent<
                        P6ThrowingEnvironmentUpdateTarget>();

                runtime.SetUpdateTargets(
                    target,
                    throwingTarget);

                var beforeFailureDispatch =
                    target.UpdateCount;

                Expect(
                    runtime.RequestManualUpdate() &&
                    target.UpdateCount ==
                        beforeFailureDispatch + 1 &&
                    runtime.UpdateFailureCount == 1,
                    "one failing update target must not stop healthy targets or the environment runtime",
                    failures);

                Expect(
                    runtime.UpdateTargetCount == 2,
                    "runtime must retain distinct healthy and failing update targets",
                    failures);

                runtime.UnregisterUpdateTarget(
                    throwingTarget);

                Expect(
                    runtime.UpdateTargetCount == 1,
                    "dynamic update target unregister must remove only the requested target",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                runtime.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.state_changes",
                        out var changes) &&
                    Math.Abs(changes - 2.0) <
                    0.001,
                    "environment diagnostics must expose state change count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.space_targets",
                        out var spaceTargetCount) &&
                    Math.Abs(
                        spaceTargetCount - 1.0) <
                    0.001,
                    "environment diagnostics must expose de-duplicated space target count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.space_mode",
                        out var spaceModeMetric) &&
                    Math.Abs(
                        spaceModeMetric -
                        (int)EnvironmentSpaceMode.Character) <
                    0.001,
                    "environment diagnostics must expose the active space mode",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.lighting_targets",
                        out var lightingTargets) &&
                    Math.Abs(
                        lightingTargets - 1.0) <
                    0.001,
                    "environment diagnostics must expose de-duplicated lighting target count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.lighting_weight",
                        out var lightingWeight) &&
                    Math.Abs(
                        lightingWeight - 0.5) <
                    0.001,
                    "environment diagnostics must expose lighting profile weight",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.lighting_failures",
                        out var lightingFailures) &&
                    lightingFailures < 0.5,
                    "valid environment lighting application must not report failures",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_targets",
                        out var transitionTargets) &&
                    Math.Abs(
                        transitionTargets - 1.0) <
                    0.001,
                    "environment diagnostics must expose de-duplicated transition target count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_count",
                        out var transitionCount) &&
                    Math.Abs(
                        transitionCount - 1.0) <
                    0.001,
                    "environment diagnostics must expose non-Cut transition count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_ticks",
                        out var transitionTicks) &&
                    Math.Abs(
                        transitionTicks - 2.0) <
                    0.001,
                    "environment diagnostics must expose transition progress tick count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_failures",
                        out var transitionFailures) &&
                    transitionFailures < 0.5,
                    "valid transition target must not produce transition failures",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.state_dispatches",
                        out var stateDispatches) &&
                    Math.Abs(
                        stateDispatches - 2.0) <
                    0.001,
                    "environment diagnostics must expose state dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.manual_dispatches",
                        out var manualDispatches) &&
                    Math.Abs(
                        manualDispatches - 2.0) <
                    0.001,
                    "environment diagnostics must expose manual dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.scheduled_dispatches",
                        out var scheduledDispatches) &&
                    Math.Abs(
                        scheduledDispatches - 2.0) <
                    0.001,
                    "environment diagnostics must expose scheduled dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.update_failures",
                        out var updateFailures) &&
                    Math.Abs(
                        updateFailures - 1.0) <
                    0.001,
                    "environment diagnostics must expose isolated update-target failures",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.update_dispatch_ms_total",
                        out var updateDispatchTotal) &&
                    updateDispatchTotal >= 0.0 &&
                    TryGetMetric(
                        metrics,
                        "environment.update_dispatch_ms_avg",
                        out var updateDispatchAverage) &&
                    updateDispatchAverage >= 0.0,
                    "environment diagnostics must attribute update-target dispatch cost",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_dispatch_count",
                        out var transitionDispatchCount) &&
                    transitionDispatchCount >= 3.0,
                    "environment diagnostics must expose actual transition-target dispatch count",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.transition_dispatch_ms_total",
                        out var transitionDispatchTotal) &&
                    transitionDispatchTotal >= 0.0 &&
                    TryGetMetric(
                        metrics,
                        "environment.transition_dispatch_ms_avg",
                        out var transitionDispatchAverage) &&
                    transitionDispatchAverage >= 0.0,
                    "environment diagnostics must attribute transition-target dispatch cost",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(root);
                }
            }
        }

        private static void ValidateDestroyedTargetLifetime(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P6 Destroyed Target Lifetime");

                var day =
                    new GameObject(
                        "Day");
                var night =
                    new GameObject(
                        "Night");
                day.transform.SetParent(
                    root.transform,
                    false);
                night.transform.SetParent(
                    root.transform,
                    false);

                var runtime =
                    root.AddComponent<
                        BasicEnvironmentRuntime>();
                runtime.Configure(
                    "environment.p6.lifetime",
                    "day",
                    EnvironmentUpdatePolicy.EventDriven,
                    EnvironmentSpaceMode.World);

                var dayBinding =
                    new EnvironmentStateBinding();
                dayBinding.Configure(
                    "day",
                    day);
                var nightBinding =
                    new EnvironmentStateBinding();
                nightBinding.Configure(
                    "night",
                    night);

                Expect(
                    runtime.ConfigureStateBindings(
                        new[]
                        {
                            dayBinding,
                            nightBinding
                        },
                        out var bindingError),
                    "destroyed target lifetime validation must configure state roots: " +
                    bindingError,
                    failures);

                var target =
                    root.AddComponent<
                        P6LifetimeEnvironmentTarget>();

                runtime.SetUpdateTargets(
                    target);
                runtime.SetSpaceTargets(
                    target);
                runtime.SetTransitionTargets(
                    target);
                runtime.SetLightingTargets(
                    target);

                var updateFailuresBefore =
                    runtime.UpdateFailureCount;

                UnityEngine.Object.DestroyImmediate(
                    target);

                Expect(
                    runtime.RequestManualUpdate() &&
                    runtime.UpdateFailureCount ==
                        updateFailuresBefore,
                    "destroyed cached environment update targets must be skipped without recurring failure accounting",
                    failures);

                Expect(
                    runtime.SetSpaceMode(
                        EnvironmentSpaceMode.Camera,
                        out var spaceError) &&
                    string.IsNullOrEmpty(
                        spaceError) &&
                    runtime.SpaceMode ==
                        EnvironmentSpaceMode.Camera,
                    "destroyed cached environment space targets must be skipped instead of invoking stale Unity interfaces: " +
                    spaceError,
                    failures);

                Expect(
                    runtime.SetLightingProfile(
                        new EnvironmentLightingProfile(
                            0.5f,
                            0.5f,
                            0.5f,
                            1f,
                            1f),
                        out var lightingError) &&
                    string.IsNullOrEmpty(
                        lightingError),
                    "destroyed cached environment lighting targets must be skipped instead of invoking stale Unity interfaces: " +
                    lightingError,
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                runtime.CollectMetrics(
                    metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "environment.lighting_failures",
                        out var lightingFailures) &&
                    lightingFailures < 0.5,
                    "destroyed environment lighting targets must not be counted as target execution failures",
                    failures);

                Expect(
                    !runtime.SetState(
                        "night",
                        new EnvironmentTransitionSpec(
                            EnvironmentTransitionMode.Crossfade,
                            0.5f),
                        out var transitionError) &&
                    !string.IsNullOrEmpty(
                        transitionError) &&
                    transitionError.Contains(
                        "live transition target",
                        StringComparison.OrdinalIgnoreCase) &&
                    day.activeSelf &&
                    !night.activeSelf,
                    "non-Cut transitions must fail closed when every cached transition target has been destroyed",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed environment target lifetime validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root);
                }
            }
        }

        private static bool TryGetMetric(
            List<RuntimeMetric> metrics,
            string name,
            out double value)
        {
            foreach (var metric in metrics)
            {
                if (string.Equals(
                    metric.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    value = metric.Value;
                    return true;
                }
            }

            value = 0.0;
            return false;
        }

        private static void Expect(
            bool condition,
            string message,
            List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }

    internal sealed class P6LifetimeEnvironmentTarget :
        MonoBehaviour,
        IEnvironmentUpdateTarget,
        IEnvironmentSpaceTarget,
        IEnvironmentTransitionTarget,
        IEnvironmentLightingTarget
    {
        public void UpdateEnvironment(
            EnvironmentUpdateContext context)
        {
        }

        public bool ValidateEnvironmentSpace(
            EnvironmentSpaceMode mode,
            out string error)
        {
            error = null;
            return true;
        }

        public void ApplyEnvironmentSpace(
            EnvironmentSpaceMode mode)
        {
        }

        public bool ValidateEnvironmentTransition(
            EnvironmentTransitionSpec transition,
            string previousStateId,
            string nextStateId,
            out string error)
        {
            error = null;
            return true;
        }

        public void ApplyEnvironmentTransition(
            EnvironmentTransitionContext context)
        {
        }

        public bool ValidateEnvironmentLighting(
            EnvironmentLightingProfile profile,
            out string error)
        {
            error = null;
            return true;
        }

        public void ApplyEnvironmentLighting(
            EnvironmentLightingProfile profile)
        {
        }
    }

    internal sealed class P6ConditionalThrowEnvironmentLightingTarget :
        MonoBehaviour,
        IEnvironmentLightingTarget
    {
        public bool ThrowOnValidate { get; set; }
        public bool ThrowOnApply { get; set; }

        public bool ValidateEnvironmentLighting(
            EnvironmentLightingProfile profile,
            out string error)
        {
            if (ThrowOnValidate)
            {
                throw new InvalidOperationException(
                    "P6 lighting validation failure");
            }

            error = null;
            return true;
        }

        public void ApplyEnvironmentLighting(
            EnvironmentLightingProfile profile)
        {
            if (ThrowOnApply)
            {
                throw new InvalidOperationException(
                    "P6 lighting apply failure");
            }
        }
    }

    internal sealed class P6FakeEnvironmentTransitionTarget :
        MonoBehaviour,
        IEnvironmentTransitionTarget
    {
        public int ApplyCount { get; private set; }
        public EnvironmentTransitionContext LastContext { get; private set; }
        public bool ThrowOnValidate { get; set; }

        public bool ValidateEnvironmentTransition(
            EnvironmentTransitionSpec transition,
            string previousStateId,
            string nextStateId,
            out string error)
        {
            if (ThrowOnValidate)
            {
                throw new InvalidOperationException(
                    "P6 transition validation failure");
            }

            error = null;
            return
                !transition.IsImmediate &&
                !string.IsNullOrEmpty(previousStateId) &&
                !string.IsNullOrEmpty(nextStateId);
        }

        public void ApplyEnvironmentTransition(
            EnvironmentTransitionContext context)
        {
            ApplyCount++;
            LastContext = context;
        }
    }

    internal sealed class P6ConditionalThrowEnvironmentSpaceTarget :
        MonoBehaviour,
        IEnvironmentSpaceTarget
    {
        public bool ValidateEnvironmentSpace(
            EnvironmentSpaceMode mode,
            out string error)
        {
            error = null;
            return true;
        }

        public void ApplyEnvironmentSpace(
            EnvironmentSpaceMode mode)
        {
            if (mode ==
                EnvironmentSpaceMode.Camera)
            {
                throw new InvalidOperationException(
                    "P6 synthetic space target apply failure");
            }
        }
    }

    internal sealed class P6ThrowingEnvironmentUpdateTarget :
        MonoBehaviour,
        IEnvironmentUpdateTarget
    {
        public void UpdateEnvironment(
            EnvironmentUpdateContext context)
        {
            throw new InvalidOperationException(
                "P6 validation target failure");
        }
    }

    internal sealed class P6FakeEnvironmentUpdateTarget :
        MonoBehaviour,
        IEnvironmentUpdateTarget
    {
        public int UpdateCount { get; private set; }
        public EnvironmentUpdateContext LastContext { get; private set; }

        public void UpdateEnvironment(
            EnvironmentUpdateContext context)
        {
            UpdateCount++;
            LastContext = context;
        }
    }
}
