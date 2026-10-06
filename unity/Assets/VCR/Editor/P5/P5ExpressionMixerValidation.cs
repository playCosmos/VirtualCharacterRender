using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P5
{
    public static class P5ExpressionMixerValidation
    {
        [MenuItem("VCR/P5/Validate Expression Mixer")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            ValidateMath(failures);
            ValidatePoseMath(failures);
            ValidateAvailability(failures);
            ValidateComponent(failures);
            ValidateIdleSmoothing(failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P5 expression mixer validation: PASS " +
                    "(expression convergence/blend modes, zero-delta smoothing stability, weighted ordered pose layers/masks, duplicate-mask last-value-wins, pose-presence bitmask compatibility, zero/full-override pose reference fast-paths, pure override expression reference reuse, pose-space guard, deterministic base/neutral fallback, pose/expression availability separation, presence isolation)");
                return true;
            }

            Debug.LogError(
                "VCR P5 expression mixer validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
            return false;
        }

        private static void ValidateMath(
            List<string> failures)
        {
            var baseState =
                CreateExpressionState(
                    aa: 0.2f,
                    new NamedExpressionValue(
                        "customA",
                        0.2f));

            var layerState =
                CreateExpressionState(
                    aa: 0.6f,
                    new NamedExpressionValue(
                        "customA",
                        0.6f),
                    new NamedExpressionValue(
                        "customB",
                        0.8f));

            var overrideState =
                ExpressionMixerMath.Blend(
                    baseState,
                    layerState,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Override);

            ExpectClose(
                overrideState.Get(
                    StandardExpression.Aa),
                0.4f,
                "override blend must lerp standard expressions",
                failures);

            ExpectClose(
                GetCustom(
                    overrideState,
                    "customA"),
                0.4f,
                "override blend must lerp matching custom expressions",
                failures);

            ExpectClose(
                GetCustom(
                    overrideState,
                    "customB"),
                0.4f,
                "override blend must weight layer-only custom expressions",
                failures);

            var additive =
                ExpressionMixerMath.Blend(
                    baseState,
                    layerState,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Additive);

            ExpectClose(
                additive.Get(
                    StandardExpression.Aa),
                0.5f,
                "additive blend must add weighted layer value",
                failures);

            var maximum =
                ExpressionMixerMath.Blend(
                    baseState,
                    layerState,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Maximum);

            ExpectClose(
                maximum.Get(
                    StandardExpression.Aa),
                0.3f,
                "maximum blend must compare base with weighted layer value",
                failures);

            ExpectClose(
                ExpressionMixerMath.ApplyDeadzone(
                    0.05f,
                    0.1f),
                0f,
                "deadzone must suppress values below threshold",
                failures);

            ExpectClose(
                ExpressionMixerMath.ApplyDeadzone(
                    0.55f,
                    0.1f),
                0.5f,
                "deadzone must rescale surviving values",
                failures);

            var alpha =
                ExpressionMixerMath.SmoothAlpha(
                    smoothingRate: 10f,
                    deltaSeconds: 0.1f);

            Expect(
                alpha > 0f &&
                alpha < 1f,
                "positive smoothing must produce an interpolation alpha inside (0, 1)",
                failures);

            var nearTarget =
                CreateExpressionState(
                    aa: 0.2005f,
                    new NamedExpressionValue(
                        "customA",
                        0.2004f));

            Expect(
                ExpressionMixerMath
                    .ApproximatelyEqual(
                        baseState,
                        nearTarget,
                        epsilon: 0.001f),
                "expression convergence check must accept values inside epsilon",
                failures);

            Expect(
                !ExpressionMixerMath
                    .ApproximatelyEqual(
                        baseState,
                        layerState,
                        epsilon: 0.001f),
                "expression convergence check must reject materially different values",
                failures);

            Expect(
                ReferenceEquals(
                    ExpressionMixerMath.Blend(
                        baseState,
                        layerState,
                        weight: 0f,
                        deadzone: 0.5f,
                        ExpressionBlendMode.Additive),
                    baseState),
                "zero-weight expression blend must preserve the immutable base-state reference without allocating a replacement state",
                failures);

            Expect(
                ReferenceEquals(
                    ExpressionMixerMath.Blend(
                        baseState: null,
                        layerState,
                        weight: 1f,
                        deadzone: 0f,
                        ExpressionBlendMode.Override),
                    layerState),
                "pure full-weight expression override without a base state must preserve the immutable layer-state reference",
                failures);

            var duplicateBase =
                CreateExpressionState(
                    aa: 0f,
                    new NamedExpressionValue(
                        "duplicate",
                        0.1f),
                    new NamedExpressionValue(
                        "duplicate",
                        0.3f));
            var duplicateLayer =
                CreateExpressionState(
                    aa: 0f,
                    new NamedExpressionValue(
                        "duplicate",
                        0.5f),
                    new NamedExpressionValue(
                        "duplicate",
                        0.7f));

            var duplicateBlend =
                ExpressionMixerMath.Blend(
                    duplicateBase,
                    duplicateLayer,
                    weight: 0.5f,
                    deadzone: 0f,
                    ExpressionBlendMode.Override);

            ExpectClose(
                GetCustom(
                    duplicateBlend,
                    "duplicate"),
                0.5f,
                "optimized custom-expression merge must preserve last-value-wins semantics for duplicate names",
                failures);

            var duplicateEffectiveTarget =
                CreateExpressionState(
                    aa: 0f,
                    new NamedExpressionValue(
                        "duplicate",
                        0.3f));

            Expect(
                ExpressionMixerMath
                    .ApproximatelyEqual(
                        duplicateBase,
                        duplicateEffectiveTarget,
                        epsilon: 0.0001f),
                "custom-expression convergence must use the same last-value-wins semantics as blending",
                failures);

            var ignoredBlankCustom =
                CreateExpressionState(
                    aa: 0f,
                    new NamedExpressionValue(
                        " ",
                        0.9f));
            var noCustom =
                CreateExpressionState(
                    aa: 0f);

            Expect(
                ExpressionMixerMath
                    .ApproximatelyEqual(
                        ignoredBlankCustom,
                        noCustom,
                        epsilon: 0.0001f),
                "blank custom-expression names ignored by blending must also be ignored by convergence checks",
                failures);
        }

        private static void ValidatePoseMath(
            List<string> failures)
        {
            var nonFiniteMask =
                new HumanoidPoseLayerMask();
            nonFiniteMask.SetDefaultBoneWeight(
                float.NaN);
            nonFiniteMask.SetRootWeights(
                float.PositiveInfinity,
                float.NegativeInfinity);
            nonFiniteMask.SetBoneWeight(
                HumanoidBoneId.LeftUpperArm,
                float.NaN);
            var nonFiniteBone =
                new HumanoidBoneWeight(
                    HumanoidBoneId.RightUpperArm,
                    float.PositiveInfinity);

            ExpectClose(
                nonFiniteMask.DefaultBoneWeight,
                1f,
                "non-finite default pose mask weight must sanitize to full weight",
                failures);
            ExpectClose(
                nonFiniteMask.RootPositionWeight,
                1f,
                "non-finite root-position pose mask weight must sanitize to full weight",
                failures);
            ExpectClose(
                nonFiniteMask.RootRotationWeight,
                1f,
                "non-finite root-rotation pose mask weight must sanitize to full weight",
                failures);
            ExpectClose(
                nonFiniteMask.GetBoneWeight(
                    HumanoidBoneId.LeftUpperArm),
                1f,
                "non-finite mask bone override must fall back to the current default bone weight",
                failures);
            ExpectClose(
                nonFiniteBone.Weight,
                0f,
                "standalone non-finite humanoid bone weight must sanitize to zero",
                failures);

            var mask =
                new HumanoidPoseLayerMask();
            mask.SetDefaultBoneWeight(0f);
            mask.SetRootWeights(
                positionWeight: 0f,
                rotationWeight: 0f);
            mask.SetBoneWeight(
                HumanoidBoneId.LeftUpperArm,
                0.5f);

            var settings =
                new HumanoidPoseLayerSettings();
            settings.Configure(
                layerEnabled: true,
                layerRole:
                    MotionLayerRole.Tracking,
                mode:
                    HumanoidPoseBlendMode.Override,
                layerWeight: 0.5f,
                layerMask: mask);

            var basePose =
                CreateTwoBonePose(
                    HumanoidPoseSpace.NormalizedLocal,
                    leftX: 0f,
                    rightX: 0.25f);

            var layerPose =
                CreateTwoBonePose(
                    HumanoidPoseSpace.NormalizedLocal,
                    leftX: 2f,
                    rightX: 4f);

            var expectedTwoBoneMask =
                HumanoidPoseState.BoneBit(
                    HumanoidBoneId.LeftUpperArm) |
                HumanoidPoseState.BoneBit(
                    HumanoidBoneId.RightUpperArm);

            Expect(
                basePose.BoneMask ==
                    expectedTwoBoneMask &&
                layerPose.BoneMask ==
                    expectedTwoBoneMask,
                "legacy bool-array pose construction must preserve bone presence in the immutable bitmask representation",
                failures);

            var mixed =
                HumanoidPoseMixerMath.Blend(
                    basePose,
                    layerPose,
                    settings,
                    out var mismatch);

            Expect(
                !mismatch &&
                mixed != null,
                "same-space pose layers must blend",
                failures);

            NormalizedBonePose left = default;
            var hasLeft =
                mixed != null &&
                mixed.TryGet(
                    HumanoidBoneId.LeftUpperArm,
                    out left);

            Expect(
                hasLeft,
                "masked left-arm pose must remain present",
                failures);

            if (hasLeft)
            {
                ExpectClose(
                    left.LocalPosition.X,
                    0.5f,
                    "global 0.5 weight multiplied by per-bone 0.5 weight must produce 0.25 effective override weight",
                    failures);
            }

            NormalizedBonePose right = default;
            var hasRight =
                mixed != null &&
                mixed.TryGet(
                    HumanoidBoneId.RightUpperArm,
                    out right);

            Expect(
                hasRight,
                "base right-arm pose must remain present",
                failures);

            if (hasRight)
            {
                ExpectClose(
                    right.LocalPosition.X,
                    0.25f,
                    "mask-excluded bones must preserve base pose",
                    failures);
            }

            settings.Configure(
                layerEnabled: true,
                layerRole:
                    MotionLayerRole.Additive,
                mode:
                    HumanoidPoseBlendMode.Additive,
                layerWeight: 0.5f,
                layerMask: mask);

            var additive =
                HumanoidPoseMixerMath.Blend(
                    basePose,
                    layerPose,
                    settings,
                    out mismatch);

            NormalizedBonePose additiveLeft = default;
            var hasAdditiveLeft =
                additive != null &&
                additive.TryGet(
                    HumanoidBoneId.LeftUpperArm,
                    out additiveLeft);

            Expect(
                hasAdditiveLeft,
                "additive pose layer must produce the included bone",
                failures);

            if (hasAdditiveLeft)
            {
                ExpectClose(
                    additiveLeft.LocalPosition.X,
                    0.5f,
                    "global and per-bone weights must multiply for additive translation",
                    failures);
            }

            mask.SetRootWeights(
                positionWeight: 0.5f,
                rotationWeight: 0f);

            var rootLayerPose =
                CreateTwoBonePose(
                    HumanoidPoseSpace.NormalizedLocal,
                    leftX: 0f,
                    rightX: 0f,
                    rootX: 4f);

            var rootWeighted =
                HumanoidPoseMixerMath.Blend(
                    basePose,
                    rootLayerPose,
                    settings,
                    out mismatch);

            ExpectClose(
                rootWeighted?.RootPosition.X ?? -1f,
                1f,
                "global 0.5 weight multiplied by root-position 0.5 weight must produce 0.25 effective root weight",
                failures);

            mask.SetRootWeights(
                positionWeight: 0f,
                rotationWeight: 0f);

            var differentSpace =
                CreateTwoBonePose(
                    HumanoidPoseSpace.OriginalLocal,
                    leftX: 5f,
                    rightX: 5f);

            settings.Configure(
                layerEnabled: true,
                layerRole:
                    MotionLayerRole.Tracking,
                mode:
                    HumanoidPoseBlendMode.Override,
                layerWeight: 0.5f,
                layerMask: mask);

            var preserved =
                HumanoidPoseMixerMath.Blend(
                    basePose,
                    differentSpace,
                    settings,
                    out mismatch);

            Expect(
                mismatch &&
                ReferenceEquals(
                    preserved,
                    basePose),
                "pose-space mismatch must preserve the base pose instead of mixing incompatible transforms",
                failures);

            var zeroMask =
                new HumanoidPoseLayerMask();
            zeroMask.SetDefaultBoneWeight(
                0f);
            zeroMask.SetRootWeights(
                positionWeight: 0f,
                rotationWeight: 0f);

            var zeroSettings =
                new HumanoidPoseLayerSettings();
            zeroSettings.Configure(
                layerEnabled: true,
                layerRole:
                    MotionLayerRole.Tracking,
                mode:
                    HumanoidPoseBlendMode.Override,
                layerWeight: 1f,
                layerMask: zeroMask);

            var zeroContribution =
                HumanoidPoseMixerMath.Blend(
                    basePose,
                    layerPose,
                    zeroSettings,
                    out mismatch);

            Expect(
                !mismatch &&
                !zeroMask.HasAnyWeight &&
                ReferenceEquals(
                    zeroContribution,
                    basePose),
                "fully zero pose masks must preserve the immutable base-pose reference without allocating replacement arrays",
                failures);

            var fullMask =
                new HumanoidPoseLayerMask();
            var fullOverrideSettings =
                new HumanoidPoseLayerSettings();
            fullOverrideSettings.Configure(
                layerEnabled: true,
                layerRole:
                    MotionLayerRole.Tracking,
                mode:
                    HumanoidPoseBlendMode.Override,
                layerWeight: 1f,
                layerMask: fullMask);

            var fullOverride =
                HumanoidPoseMixerMath.Blend(
                    basePose,
                    layerPose,
                    fullOverrideSettings,
                    out mismatch);

            Expect(
                !mismatch &&
                fullMask.HasFullWeight &&
                ReferenceEquals(
                    fullOverride,
                    layerPose),
                "full-weight pose override that covers the base pose must reuse the immutable layer-pose reference",
                failures);

            var fullOverrideWithoutBase =
                HumanoidPoseMixerMath.Blend(
                    basePose: null,
                    layerPose,
                    fullOverrideSettings,
                    out mismatch);

            Expect(
                !mismatch &&
                ReferenceEquals(
                    fullOverrideWithoutBase,
                    layerPose),
                "full-weight pose override without a base pose must reuse the immutable layer-pose reference",
                failures);

            var duplicateMask =
                new HumanoidPoseLayerMask();

            SetPrivateField(
                duplicateMask,
                "boneOverrides",
                new[]
                {
                    new HumanoidBoneWeight(
                        HumanoidBoneId.LeftUpperArm,
                        0.25f),
                    new HumanoidBoneWeight(
                        HumanoidBoneId.LeftUpperArm,
                        0.75f)
                });

            ExpectClose(
                duplicateMask.GetBoneWeight(
                    HumanoidBoneId.LeftUpperArm),
                0.75f,
                "duplicate pose-mask overrides must use the last serialized occurrence",
                failures);

            duplicateMask.SetBoneWeight(
                HumanoidBoneId.LeftUpperArm,
                0.4f);

            ExpectClose(
                duplicateMask.GetBoneWeight(
                    HumanoidBoneId.LeftUpperArm),
                0.4f,
                "pose-mask mutation must update the same last occurrence used by lookup",
                failures);
        }

        private static void ValidateAvailability(
            List<string> failures)
        {
            var timestampUs =
                MonotonicClock
                    .NowMicroseconds();

            var noFullBodyPresence =
                new TrackingPresenceSnapshot(
                    sequence:
                        timestampUs,
                    timestampUs:
                        timestampUs,
                    subjectState:
                        SubjectPresenceState.Present,
                    faceSourceAvailable:
                        true,
                    bodyHandsSourceAvailable:
                        true,
                    fullBodySourceAvailable:
                        false,
                    faceSubjectEvidence:
                        true,
                    bodyHandsSubjectEvidence:
                        true,
                    fullBodySubjectEvidence:
                        false,
                    anySourceAvailable:
                        true,
                    subjectEvidence:
                        true,
                    events:
                        TrackingPresenceEvents.None);

            var expressionOnly =
                MotionApplicationAvailabilityResolver
                    .Resolve(
                        noFullBodyPresence,
                        hasPoseFrame: true,
                        hasExpressionFrame: true);

            Expect(
                !expressionOnly.PoseAvailable,
                "full-body pose application must remain unavailable without full-body presence",
                failures);

            Expect(
                expressionOnly.ExpressionsAvailable,
                "expression application must remain available when an expression frame exists without full-body presence",
                failures);

            var finalMixPose =
                MotionApplicationAvailabilityResolver
                    .Resolve(
                        noFullBodyPresence,
                        hasPoseFrame: true,
                        hasExpressionFrame: true,
                        finalMixOwnsPoseAvailability:
                            true);

            Expect(
                finalMixPose.PoseAvailable &&
                finalMixPose.ExpressionsAvailable,
                "final mix providers must own mixed-pose availability so procedural/base pose layers are not re-gated by raw full-body presence",
                failures);

            var lostSubject =
                new TrackingPresenceSnapshot(
                    sequence:
                        timestampUs + 1,
                    timestampUs:
                        timestampUs + 1,
                    subjectState:
                        SubjectPresenceState.Lost,
                    faceSourceAvailable:
                        false,
                    bodyHandsSourceAvailable:
                        false,
                    fullBodySourceAvailable:
                        false,
                    faceSubjectEvidence:
                        false,
                    bodyHandsSubjectEvidence:
                        false,
                    fullBodySubjectEvidence:
                        false,
                    anySourceAvailable:
                        false,
                    subjectEvidence:
                        false,
                    events:
                        TrackingPresenceEvents.SubjectLost);

            var audioFallback =
                MotionApplicationAvailabilityResolver
                    .Resolve(
                        lostSubject,
                        hasPoseFrame: false,
                        hasExpressionFrame: true);

            Expect(
                !audioFallback.PoseAvailable &&
                audioFallback.ExpressionsAvailable,
                "expression-only fallback must not become coupled to visual subject/full-body presence",
                failures);
        }

        private static void ValidateComponent(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P5 Expression Mixer Validation");

                var route =
                    root.AddComponent<
                        P5FakeRouteProvider>();
                var layer =
                    root.AddComponent<
                        P5FakeExpressionProvider>();
                var poseOverrideLayer =
                    root.AddComponent<
                        P5FakeExpressionProvider>();
                var poseAdditiveLayer =
                    root.AddComponent<
                        P5FakeExpressionProvider>();
                var mixer =
                    root.AddComponent<
                        MotionExpressionMixer>();

                var nowUs =
                    MonotonicClock
                        .NowMicroseconds();

                route.FaceFrame =
                    CreateFaceFrame(
                        "route-face",
                        sequence: 1,
                        nowUs);
                route.ExpressionFrame =
                    CreateExpressionFrame(
                        "route-expression",
                        sequence: 1,
                        nowUs,
                        aa: 0.2f);
                route.PoseFrame =
                    CreatePoseFrame(
                        "route-pose",
                        sequence: 1,
                        nowUs,
                        leftX: 0f,
                        rightX: 0.25f);
                route.Presence =
                    CreatePresence(
                        nowUs);

                layer.ExpressionFrame =
                    CreateExpressionFrame(
                        "overlay-expression",
                        sequence: 1,
                        nowUs,
                        aa: 0.6f);
                layer.PoseFrame =
                    CreatePoseFrame(
                        "overlay-pose",
                        sequence: 1,
                        nowUs,
                        leftX: 2f,
                        rightX: 4f);

                poseOverrideLayer.PoseFrame =
                    CreatePoseFrame(
                        "ordered-override-pose",
                        sequence: 1,
                        nowUs,
                        leftX: 0f,
                        rightX: 1f);

                poseAdditiveLayer.PoseFrame =
                    CreatePoseFrame(
                        "ordered-additive-pose",
                        sequence: 1,
                        nowUs,
                        leftX: 0f,
                        rightX: 0.5f);

                var poseMask =
                    new HumanoidPoseLayerMask();
                poseMask.SetDefaultBoneWeight(0f);
                poseMask.SetRootWeights(
                    positionWeight: 0f,
                    rotationWeight: 0f);
                poseMask.SetBoneWeight(
                    HumanoidBoneId.LeftUpperArm,
                    0.5f);

                var poseSettings =
                    new HumanoidPoseLayerSettings();
                poseSettings.Configure(
                    layerEnabled: true,
                    layerRole:
                        MotionLayerRole.Additive,
                    mode:
                        HumanoidPoseBlendMode.Override,
                    layerWeight: 0.5f,
                    layerMask: poseMask);

                var rightMask =
                    new HumanoidPoseLayerMask();
                rightMask.SetDefaultBoneWeight(0f);
                rightMask.SetRootWeights(
                    positionWeight: 0f,
                    rotationWeight: 0f);
                rightMask.SetBoneWeight(
                    HumanoidBoneId.RightUpperArm,
                    1f);

                var overrideSettings =
                    new HumanoidPoseLayerSettings();
                overrideSettings.Configure(
                    layerEnabled: true,
                    layerRole:
                        MotionLayerRole.Tracking,
                    mode:
                        HumanoidPoseBlendMode.Override,
                    layerWeight: 1f,
                    layerMask: rightMask);

                var additiveSettings =
                    new HumanoidPoseLayerSettings();
                additiveSettings.Configure(
                    layerEnabled: true,
                    layerRole:
                        MotionLayerRole.Procedural,
                    mode:
                        HumanoidPoseBlendMode.Additive,
                    layerWeight: 1f,
                    layerMask: rightMask);

                var overrideSlot =
                    new HumanoidPoseLayerSlot();
                overrideSlot.Configure(
                    poseOverrideLayer,
                    overrideSettings);

                var additiveSlot =
                    new HumanoidPoseLayerSlot();
                additiveSlot.Configure(
                    poseAdditiveLayer,
                    additiveSettings);

                mixer.SetRoutedProvider(
                    route);
                mixer.SetPoseLayerProvider(
                    layer);
                mixer.ConfigurePoseLayer(
                    poseSettings);
                mixer.SetAdditionalPoseLayers(
                    overrideSlot,
                    additiveSlot);

                var exportedPrimaryMask =
                    poseSettings.Mask;
                exportedPrimaryMask.SetBoneWeight(
                    HumanoidBoneId.LeftUpperArm,
                    0f);

                var exportedOverrideSettings =
                    overrideSlot.Settings;
                exportedOverrideSettings.Configure(
                    layerEnabled: false,
                    layerRole:
                        MotionLayerRole.Tracking,
                    mode:
                        HumanoidPoseBlendMode.Override,
                    layerWeight: 0f,
                    layerMask:
                        rightMask);

                ExpectClose(
                    poseSettings.Mask.GetBoneWeight(
                        HumanoidBoneId.LeftUpperArm),
                    0.5f,
                    "public pose-layer mask access must return a defensive copy",
                    failures);
                ExpectClose(
                    overrideSlot.Settings.Weight,
                    1f,
                    "public pose-layer slot settings access must return a defensive copy",
                    failures);

                poseMask.SetBoneWeight(
                    HumanoidBoneId.LeftUpperArm,
                    0f);
                poseSettings.Configure(
                    layerEnabled: false,
                    layerRole:
                        MotionLayerRole.Additive,
                    mode:
                        HumanoidPoseBlendMode.Override,
                    layerWeight: 0f,
                    layerMask:
                        poseMask);
                rightMask.SetBoneWeight(
                    HumanoidBoneId.RightUpperArm,
                    0f);
                overrideSettings.Configure(
                    layerEnabled: false,
                    layerRole:
                        MotionLayerRole.Tracking,
                    mode:
                        HumanoidPoseBlendMode.Override,
                    layerWeight: 0f,
                    layerMask:
                        rightMask);
                additiveSettings.Configure(
                    layerEnabled: false,
                    layerRole:
                        MotionLayerRole.Procedural,
                    mode:
                        HumanoidPoseBlendMode.Additive,
                    layerWeight: 0f,
                    layerMask:
                        rightMask);
                overrideSlot.Configure(
                    null,
                    overrideSettings);
                additiveSlot.Configure(
                    null,
                    additiveSettings);

                var nonFinitePoseSettings =
                    new HumanoidPoseLayerSettings();
                nonFinitePoseSettings.Configure(
                    layerEnabled: true,
                    layerRole:
                        MotionLayerRole.Tracking,
                    mode:
                        HumanoidPoseBlendMode.Override,
                    layerWeight:
                        float.NaN,
                    layerMask:
                        poseMask);

                ExpectClose(
                    nonFinitePoseSettings.Weight,
                    1f,
                    "non-finite pose-layer weight must sanitize to the default full weight",
                    failures);

                mixer.SetExpressionLayerProvider(
                    layer);
                mixer.ConfigureExpressionLayer(
                    ExpressionBlendMode.Additive,
                    weight:
                        float.NaN,
                    deadzone:
                        float.PositiveInfinity,
                    smoothing:
                        float.NegativeInfinity);

                ExpectClose(
                    mixer.ExpressionLayerWeight,
                    1f,
                    "non-finite expression layer weight must sanitize to the default full weight",
                    failures);
                ExpectClose(
                    GetPrivateField<float>(
                        mixer,
                        "expressionDeadzone"),
                    0f,
                    "non-finite expression deadzone must sanitize to zero",
                    failures);
                ExpectClose(
                    GetPrivateField<float>(
                        mixer,
                        "expressionSmoothing"),
                    0f,
                    "non-finite expression smoothing must sanitize to zero",
                    failures);
                Expect(
                    !mixer.ExpressionsPreSmoothed,
                    "sanitized non-finite expression smoothing must not claim smoothing ownership",
                    failures);

                mixer.ConfigureExpressionLayer(
                    ExpressionBlendMode.Additive,
                    weight: 0.5f,
                    deadzone: 0f,
                    smoothing: 0f);

                InvokeUpdate(mixer);

                Expect(
                    poseOverrideLayer.PoseReadCount == 1 &&
                    poseAdditiveLayer.PoseReadCount == 1,
                    "each additional pose provider must be sampled once per mixer update and reused for change detection plus blending",
                    failures);

                Expect(
                    mixer is ITrackingMixProvider &&
                    mixer is ITrackingRouteProvider,
                    "mixer must satisfy final mix and routed provider contracts",
                    failures);

                Expect(
                    mixer.Presence.SubjectState ==
                    route.Presence.SubjectState,
                    "mixer presence must pass through routed visual presence",
                    failures);

                Expect(
                    mixer.TryGetLatestFace(
                        out var face) &&
                    ReferenceEquals(
                        face,
                        route.FaceFrame),
                    "face frames must pass through unchanged in the first P5 mixer slice",
                    failures);

                var hasMixedPose =
                    mixer.TryGetLatestHumanoidPose(
                        out var mixedPoseFrame) &&
                    mixedPoseFrame?.HumanoidPose != null;

                NormalizedBonePose mixedLeft = default;
                var hasMixedLeft =
                    hasMixedPose &&
                    mixedPoseFrame.HumanoidPose.TryGet(
                        HumanoidBoneId.LeftUpperArm,
                        out mixedLeft);

                Expect(
                    hasMixedLeft,
                    "mixer must emit a weighted humanoid-pose frame when a pose layer is configured",
                    failures);

                if (hasMixedLeft)
                {
                    ExpectClose(
                        mixedLeft.LocalPosition.X,
                        0.5f,
                        "component pose layer must multiply global and per-bone weights",
                        failures);
                }

                NormalizedBonePose mixedRight = default;
                var hasMixedRight =
                    hasMixedPose &&
                    mixedPoseFrame.HumanoidPose.TryGet(
                        HumanoidBoneId.RightUpperArm,
                        out mixedRight);

                Expect(
                    hasMixedRight,
                    "ordered pose layers must produce the right-arm result",
                    failures);

                if (hasMixedRight)
                {
                    ExpectClose(
                        mixedRight.LocalPosition.X,
                        1.5f,
                        "ordered pose layers must apply override before additive in array order",
                        failures);
                }

                Expect(
                    poseOverrideLayer.PoseReadCount == 1 &&
                    poseAdditiveLayer.PoseReadCount == 1,
                    "mixer-owned pose-layer settings and slots must remain stable after caller-owned masks/settings/slots are mutated",
                    failures);

                var orderedMetrics =
                    new List<RuntimeMetric>();
                mixer.CollectMetrics(
                    orderedMetrics);

                Expect(
                    TryGetMetric(
                        orderedMetrics,
                        "mixer.pose.additional_layers",
                        out var additionalLayerCount) &&
                    Math.Abs(
                        additionalLayerCount - 2.0) <
                    0.001,
                    "mixer diagnostics must expose the configured ordered pose-layer count",
                    failures);

                poseOverrideLayer.PoseFrame =
                    CreatePoseFrame(
                        "ordered-override-mismatch",
                        sequence: 2,
                        nowUs + 1,
                        leftX: 0f,
                        rightX: 8f,
                        poseSpace:
                            HumanoidPoseSpace.OriginalLocal);

                InvokeUpdate(mixer);

                Expect(
                    poseOverrideLayer.PoseReadCount == 2 &&
                    poseAdditiveLayer.PoseReadCount == 2,
                    "additional pose provider reads must remain one-per-update when a layer changes",
                    failures);

                var hasMismatchPose =
                    mixer.TryGetLatestHumanoidPose(
                        out var mismatchPoseFrame) &&
                    mismatchPoseFrame?.HumanoidPose != null;

                NormalizedBonePose mismatchRight = default;
                var hasMismatchRight =
                    hasMismatchPose &&
                    mismatchPoseFrame.HumanoidPose.TryGet(
                        HumanoidBoneId.RightUpperArm,
                        out mismatchRight);

                Expect(
                    hasMismatchRight,
                    "a pose-space mismatch in one ordered layer must preserve the previously mixed pose and continue later compatible layers",
                    failures);

                if (hasMismatchRight)
                {
                    ExpectClose(
                        mismatchRight.LocalPosition.X,
                        0.75f,
                        "mismatched ordered layer must be skipped while the following additive layer still applies",
                        failures);
                }

                orderedMetrics.Clear();
                mixer.CollectMetrics(
                    orderedMetrics);

                Expect(
                    TryGetMetric(
                        orderedMetrics,
                        "mixer.pose.space_mismatch",
                        out var orderedMismatch) &&
                    orderedMismatch > 0.5,
                    "ordered-layer pose-space mismatch must be surfaced in mixer diagnostics",
                    failures);

                mixer.SetAdditionalPoseLayers();
                mixer.SetPoseLayerProvider(
                    null);
                InvokeUpdate(mixer);

                Expect(
                    mixer.TryGetLatestHumanoidPose(
                        out var basePoseOnly) &&
                    ReferenceEquals(
                        basePoseOnly,
                        route.PoseFrame),
                    "missing pose overlay must fall back to the routed base pose",
                    failures);

                var sameSequenceReplacementPose =
                    CreatePoseFrame(
                        "route-pose",
                        sequence: 1,
                        nowUs + 2,
                        leftX: 0.75f,
                        rightX: 0.5f);
                route.PoseFrame =
                    sameSequenceReplacementPose;

                InvokeUpdate(mixer);

                Expect(
                    mixer.TryGetLatestHumanoidPose(
                        out var replacementPoseOutput) &&
                    ReferenceEquals(
                        replacementPoseOutput,
                        sameSequenceReplacementPose),
                    "a new immutable pose frame must be observed even when source id and sequence are reused",
                    failures);

                route.PoseFrame = null;
                InvokeUpdate(mixer);

                Expect(
                    !mixer.TryGetLatestHumanoidPose(
                        out _),
                    "missing base and pose overlay must emit no pose frame so the target can return to neutral",
                    failures);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var mixed) &&
                    mixed?.Expressions != null,
                    "mixer must emit an expression frame",
                    failures);

                ExpectClose(
                    mixed?.Expressions?.Get(
                        StandardExpression.Aa) ??
                    -1f,
                    0.5f,
                    "component additive blend must combine routed base and overlay",
                    failures);

                mixer.SetExpressionLayerProvider(
                    null);
                InvokeUpdate(mixer);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var baseOnly) &&
                    baseOnly?.Expressions != null &&
                    ReferenceEquals(
                        baseOnly,
                        route.ExpressionFrame),
                    "unmodified base expression frame must pass through without a mixer envelope when no overlay or smoothing is active",
                    failures);

                ExpectClose(
                    baseOnly?.Expressions?.Get(
                        StandardExpression.Aa) ??
                    -1f,
                    0.2f,
                    "missing overlay must preserve routed base expressions exactly",
                    failures);

                route.ExpressionFrame =
                    CreateExpressionFrame(
                        "route-expression",
                        sequence: 1,
                        nowUs + 3,
                        aa: 0.35f);

                InvokeUpdate(mixer);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var sameSequenceExpression) &&
                    sameSequenceExpression?.Expressions != null &&
                    ReferenceEquals(
                        sameSequenceExpression,
                        route.ExpressionFrame),
                    "a new immutable expression frame must be observed and passed through even when source id and sequence are reused",
                    failures);

                ExpectClose(
                    sameSequenceExpression?.Expressions?.Get(
                        StandardExpression.Aa) ??
                    -1f,
                    0.35f,
                    "same-sequence replacement expression must refresh mixer output by frame identity",
                    failures);

                route.ExpressionFrame = null;
                InvokeUpdate(mixer);

                Expect(
                    !mixer.TryGetLatestExpressions(
                        out _),
                    "missing base and expression overlay must emit no expression frame so the target can return to neutral",
                    failures);

                Expect(
                    !mixer.HumanoidPosePreSmoothed &&
                    !mixer.ExpressionsPreSmoothed,
                    "default mixer configuration must not claim pre-smoothed pose or expressions",
                    failures);

                var metrics =
                    new List<RuntimeMetric>();
                mixer.CollectMetrics(metrics);

                Expect(
                    TryGetMetric(
                        metrics,
                        "mixer.pose.weight",
                        out var poseWeight) &&
                    Math.Abs(
                        poseWeight - 0.5) <
                    0.001,
                    "mixer diagnostics must expose pose layer weight",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "mixer.pose.space_mismatch",
                        out var poseMismatch) &&
                    poseMismatch < 0.5,
                    "compatible pose layer must not report a pose-space mismatch",
                    failures);

                Expect(
                    TryGetMetric(
                        metrics,
                        "mixer.expression.weight",
                        out var weight) &&
                    Math.Abs(weight - 0.5) <
                    0.001,
                    "mixer diagnostics must expose expression layer weight",
                    failures);

                mixer.ConfigureExpressionLayer(
                    ExpressionBlendMode.Additive,
                    weight: 0.5f,
                    deadzone: 0f,
                    smoothing: 8f);

                Expect(
                    mixer.ExpressionsPreSmoothed,
                    "enabling mixer expression smoothing must transfer smoothing ownership to the final mix provider",
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

            ValidateDestroyedProviderRecovery(
                failures);
        }

        private static void ValidateIdleSmoothing(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P5 Idle Smoothing Validation");

                var mixer =
                    root.AddComponent<
                        MotionExpressionMixer>();

                mixer.ConfigureExpressionLayer(
                    ExpressionBlendMode.Override,
                    weight: 1f,
                    deadzone: 0f,
                    smoothing: 8f);

                SetPrivateField(
                    mixer,
                    "_targetExpressions",
                    CreateExpressionState(
                        aa: 1f));
                SetPrivateField(
                    mixer,
                    "_outputDirty",
                    true);

                InvokeExpressionOutput(
                    mixer,
                    deltaSeconds: 0f);

                Expect(
                    !mixer.TryGetLatestExpressions(
                        out _),
                    "zero-delta smoothing must not publish an unchanged placeholder frame before time advances",
                    failures);

                InvokeExpressionOutput(
                    mixer,
                    deltaSeconds: 0.1f);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var progressed) &&
                    progressed?.Expressions != null,
                    "positive-delta smoothing must publish a progressed expression frame",
                    failures);

                InvokeExpressionOutput(
                    mixer,
                    deltaSeconds: 0f);

                Expect(
                    mixer.TryGetLatestExpressions(
                        out var paused) &&
                    ReferenceEquals(
                        progressed,
                        paused),
                    "zero-delta smoothing must retain the last published frame instead of allocating an identical replacement frame",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "idle expression smoothing validation unexpected exception: " +
                    exception);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            root);
                }
            }
        }

        private static void ValidateDestroyedProviderRecovery(
            List<string> failures)
        {
            GameObject root = null;

            try
            {
                root =
                    new GameObject(
                        "P5 Destroyed Provider Recovery");

                var oldRoute =
                    root.AddComponent<
                        P5FakeRouteProvider>();
                var mixer =
                    root.AddComponent<
                        MotionExpressionMixer>();

                mixer.SetRoutedProvider(
                    oldRoute);

                UnityEngine.Object.DestroyImmediate(
                    oldRoute);

                var replacement =
                    root.AddComponent<
                        P5FakeRouteProvider>();
                var nowUs =
                    MonotonicClock
                        .NowMicroseconds();
                replacement.FaceFrame =
                    CreateFaceFrame(
                        "replacement-route",
                        1,
                        nowUs);
                replacement.Presence =
                    CreatePresence(
                        nowUs);

                InvokeUpdate(
                    mixer);

                Expect(
                    mixer.TryGetLatestFace(
                        out var recovered) &&
                    ReferenceEquals(
                        recovered,
                        replacement.FaceFrame),
                    "mixer must discard a destroyed routed provider and auto-discover a live replacement",
                    failures);

                Expect(
                    mixer.Presence.SubjectState ==
                        replacement.Presence.SubjectState,
                    "mixer presence must rebind with the recovered routed provider",
                    failures);
            }
            catch (Exception exception)
            {
                failures.Add(
                    "destroyed mixer provider recovery unexpected exception: " +
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

        private static NormalizedExpressionState
            CreateExpressionState(
                float aa,
                params NamedExpressionValue[] custom)
        {
            var standard =
                new float[
                    (int)StandardExpression.Count];

            standard[
                (int)StandardExpression.Aa] =
                aa;

            return new NormalizedExpressionState(
                standard,
                custom);
        }

        private static HumanoidPoseState
            CreateTwoBonePose(
                HumanoidPoseSpace poseSpace,
                float leftX,
                float rightX,
                float rootX = 0f)
        {
            var bones =
                new NormalizedBonePose[
                    (int)HumanoidBoneId.Count];
            var hasBone =
                new bool[
                    (int)HumanoidBoneId.Count];

            var left =
                (int)HumanoidBoneId.LeftUpperArm;
            var right =
                (int)HumanoidBoneId.RightUpperArm;

            bones[left] =
                new NormalizedBonePose(
                    new TrackingVector3(
                        leftX,
                        0f,
                        0f),
                    TrackingQuaternion.Identity);
            hasBone[left] = true;

            bones[right] =
                new NormalizedBonePose(
                    new TrackingVector3(
                        rightX,
                        0f,
                        0f),
                    TrackingQuaternion.Identity);
            hasBone[right] = true;

            return new HumanoidPoseState(
                poseSpace,
                new TrackingVector3(
                    rootX,
                    0f,
                    0f),
                TrackingQuaternion.Identity,
                bones,
                hasBone);
        }

        private static TrackingFrame CreatePoseFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs,
            float leftX,
            float rightX,
            HumanoidPoseSpace poseSpace =
                HumanoidPoseSpace.NormalizedLocal)
        {
            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.FullBody,
                confidence:
                    1f,
                subjectDetected:
                    true,
                humanoidPose:
                    CreateTwoBonePose(
                        poseSpace,
                        leftX,
                        rightX),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingFrame
            CreateExpressionFrame(
                string sourceId,
                long sequence,
                long runtimeTimestampUs,
                float aa)
        {
            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.Expressions,
                confidence:
                    1f,
                subjectDetected:
                    false,
                expressions:
                    CreateExpressionState(
                        aa),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingFrame CreateFaceFrame(
            string sourceId,
            long sequence,
            long runtimeTimestampUs)
        {
            var coefficients =
                new float[
                    (int)FaceCoefficient.Count];

            return new TrackingFrame(
                sequence,
                sourceTimestampUs:
                    runtimeTimestampUs,
                validRegions:
                    TrackingRegion.Face |
                    TrackingRegion.Head,
                confidence:
                    1f,
                subjectDetected:
                    true,
                face:
                    new NormalizedFaceState(
                        TrackingQuaternion.Identity,
                        TrackingVector3.Zero,
                        coefficients),
                sourceId:
                    sourceId,
                runtimeTimestampUs:
                    runtimeTimestampUs);
        }

        private static TrackingPresenceSnapshot
            CreatePresence(
                long timestampUs)
        {
            return new TrackingPresenceSnapshot(
                sequence:
                    timestampUs,
                timestampUs:
                    timestampUs,
                subjectState:
                    SubjectPresenceState.Present,
                faceSourceAvailable:
                    true,
                bodyHandsSourceAvailable:
                    false,
                fullBodySourceAvailable:
                    false,
                faceSubjectEvidence:
                    true,
                bodyHandsSubjectEvidence:
                    false,
                fullBodySubjectEvidence:
                    false,
                anySourceAvailable:
                    true,
                subjectEvidence:
                    true,
                events:
                    TrackingPresenceEvents.None);
        }

        private static void InvokeUpdate(
            MotionExpressionMixer mixer)
        {
            var method =
                typeof(MotionExpressionMixer)
                    .GetMethod(
                        "Update",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(MotionExpressionMixer)
                        .FullName,
                    "Update");
            }

            method.Invoke(
                mixer,
                null);
        }

        private static void InvokeExpressionOutput(
            MotionExpressionMixer mixer,
            float deltaSeconds)
        {
            var method =
                typeof(MotionExpressionMixer)
                    .GetMethod(
                        "UpdateExpressionOutput",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(MotionExpressionMixer)
                        .FullName,
                    "UpdateExpressionOutput");
            }

            method.Invoke(
                mixer,
                new object[]
                {
                    deltaSeconds
                });
        }

        private static T GetPrivateField<T>(
            object target,
            string fieldName)
        {
            var field =
                target.GetType()
                    .GetField(
                        fieldName,
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    target.GetType().FullName,
                    fieldName);
            }

            return (T)field.GetValue(
                target);
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field =
                target.GetType()
                    .GetField(
                        fieldName,
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    target.GetType().FullName,
                    fieldName);
            }

            field.SetValue(
                target,
                value);
        }

        private static float GetCustom(
            NormalizedExpressionState state,
            string name)
        {
            if (state == null)
            {
                return 0f;
            }

            foreach (var item in state.Custom)
            {
                if (string.Equals(
                    item.Name,
                    name,
                    StringComparison.Ordinal))
                {
                    return item.Value;
                }
            }

            return 0f;
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

        private static void ExpectClose(
            float actual,
            float expected,
            string message,
            List<string> failures)
        {
            if (Math.Abs(
                    actual - expected) >
                0.001f)
            {
                failures.Add(
                    message +
                    $" (expected={expected:F3}, actual={actual:F3})");
            }
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

    internal sealed class P5FakeRouteProvider :
        MonoBehaviour,
        ITrackingRouteProvider
    {
        public TrackingFrame FaceFrame { get; set; }
        public TrackingFrame PoseFrame { get; set; }
        public TrackingFrame ExpressionFrame { get; set; }
        public TrackingPresenceSnapshot Presence { get; set; }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = FaceFrame;
            return frame != null;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            frame = PoseFrame;
            return frame != null;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = ExpressionFrame;
            return frame != null;
        }
    }

    internal sealed class P5FakeExpressionProvider :
        MonoBehaviour,
        ITrackingFrameProvider
    {
        public TrackingFrame PoseFrame { get; set; }
        public TrackingFrame ExpressionFrame { get; set; }
        public int PoseReadCount { get; private set; }

        public bool TryGetLatestFace(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestBodyHands(
            out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestHumanoidPose(
            out TrackingFrame frame)
        {
            PoseReadCount++;
            frame = PoseFrame;
            return frame != null;
        }

        public bool TryGetLatestExpressions(
            out TrackingFrame frame)
        {
            frame = ExpressionFrame;
            return frame != null;
        }
    }
}
