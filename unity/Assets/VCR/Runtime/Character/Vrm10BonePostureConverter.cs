using System;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Character
{
    /// <summary>
    /// Converts VMC/VRM original humanoid local rotations into the normalized
    /// local-rotation space consumed by UniVRM's ControlRig.
    ///
    /// Capture must happen while the target model is still at its initial pose.
    /// </summary>
    public sealed class Vrm10BonePostureConverter
    {
        private readonly BoneInitialRotation[] _initial =
            new BoneInitialRotation[(int)HumanoidBoneId.Count];
        private readonly bool[] _hasInitial =
            new bool[(int)HumanoidBoneId.Count];

        public bool IsIdentity { get; private set; } = true;

        public static Vrm10BonePostureConverter Capture(
            Vrm10Instance instance)
        {
            var converter =
                new Vrm10BonePostureConverter();

            if (instance == null ||
                instance.Humanoid == null)
            {
                return converter;
            }

            for (var i = 0; i < (int)HumanoidBoneId.Count; i++)
            {
                var boneId = (HumanoidBoneId)i;

                if (!Enum.TryParse(
                        boneId.ToString(),
                        ignoreCase: false,
                        out HumanBodyBones unityBone) ||
                    unityBone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                var transform =
                    instance.Humanoid.GetBoneTransform(
                        unityBone);

                if (transform == null)
                {
                    continue;
                }

                var initial =
                    new BoneInitialRotation(transform);

                converter._initial[i] = initial;
                converter._hasInitial[i] = true;

                if (Quaternion.Angle(
                        initial.InitialLocalRotation,
                        Quaternion.identity) > 0.001f ||
                    Quaternion.Angle(
                        initial.InitialGlobalRotation,
                        Quaternion.identity) > 0.001f)
                {
                    converter.IsIdentity = false;
                }
            }

            return converter;
        }

        public Quaternion ToNormalizedLocalRotation(
            HumanoidBoneId bone,
            Quaternion originalLocalRotation)
        {
            var index = (int)bone;
            if (index < 0 ||
                index >= _initial.Length ||
                !_hasInitial[index])
            {
                return originalLocalRotation;
            }

            var initial = _initial[index];

            return
                initial.InitialGlobalRotation *
                Quaternion.Inverse(
                    initial.InitialLocalRotation) *
                originalLocalRotation *
                Quaternion.Inverse(
                    initial.InitialGlobalRotation);
        }
    }
}
