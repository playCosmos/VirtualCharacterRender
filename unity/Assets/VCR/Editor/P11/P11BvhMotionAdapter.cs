using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Editor.P11
{
    internal sealed class P11BvhMotionAdapter :
        IP11ExternalMotionAdapter
    {
        private sealed class Joint
        {
            public string Name;
            public int ChannelStart;
            public string[] Channels =
                Array.Empty<string>();
        }

        private sealed class ParsedBvh
        {
            public Joint Root;
            public Joint[] Joints =
                Array.Empty<Joint>();
            public int ChannelCount;
            public int FrameCount;
            public float FrameTime;
            public float[][] Frames =
                Array.Empty<float[]>();
        }

        private sealed class TokenReader
        {
            private readonly string[] _tokens;
            private int _index;

            public TokenReader(
                string text)
            {
                _tokens =
                    Tokenize(
                        text);
            }

            public bool End =>
                _index >=
                _tokens.Length;

            public string Peek() =>
                End
                    ? null
                    : _tokens[
                        _index];

            public string Read()
            {
                if (End)
                {
                    throw new FormatException(
                        "Unexpected end of BVH file.");
                }

                return _tokens[
                    _index++];
            }

            public void Expect(
                string expected)
            {
                var actual =
                    Read();

                if (!string.Equals(
                        actual,
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException(
                        $"Expected BVH token '{expected}' but found '{actual}'.");
                }
            }

            public int ReadInt()
            {
                var token =
                    Read();

                if (!int.TryParse(
                        token,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    throw new FormatException(
                        $"Invalid BVH integer '{token}'.");
                }

                return value;
            }

            public float ReadFloat()
            {
                var token =
                    Read();

                if (!float.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value) ||
                    float.IsNaN(
                        value) ||
                    float.IsInfinity(
                        value))
                {
                    throw new FormatException(
                        $"Invalid BVH number '{token}'.");
                }

                return value;
            }

            private static string[] Tokenize(
                string text)
            {
                var tokens =
                    new List<string>();
                var current =
                    new System.Text.StringBuilder();

                void Flush()
                {
                    if (current.Length <= 0)
                    {
                        return;
                    }

                    tokens.Add(
                        current.ToString());
                    current.Length = 0;
                }

                foreach (var ch in
                         text ??
                         string.Empty)
                {
                    if (char.IsWhiteSpace(
                            ch))
                    {
                        Flush();
                        continue;
                    }

                    if (ch == '{' ||
                        ch == '}')
                    {
                        Flush();
                        tokens.Add(
                            ch.ToString());
                        continue;
                    }

                    current.Append(
                        ch);
                }

                Flush();
                return tokens.ToArray();
            }
        }

        private static readonly Dictionary<
            string,
            HumanoidBoneId> BoneAliases =
                new(
                    StringComparer.Ordinal)
                {
                    ["hips"] = HumanoidBoneId.Hips,
                    ["hip"] = HumanoidBoneId.Hips,
                    ["pelvis"] = HumanoidBoneId.Hips,
                    ["root"] = HumanoidBoneId.Hips,
                    ["hipcenter"] = HumanoidBoneId.Hips,

                    ["leftupleg"] = HumanoidBoneId.LeftUpperLeg,
                    ["leftupperleg"] = HumanoidBoneId.LeftUpperLeg,
                    ["leftthigh"] = HumanoidBoneId.LeftUpperLeg,
                    ["lthigh"] = HumanoidBoneId.LeftUpperLeg,
                    ["rightupleg"] = HumanoidBoneId.RightUpperLeg,
                    ["rightupperleg"] = HumanoidBoneId.RightUpperLeg,
                    ["rightthigh"] = HumanoidBoneId.RightUpperLeg,
                    ["rthigh"] = HumanoidBoneId.RightUpperLeg,

                    ["leftleg"] = HumanoidBoneId.LeftLowerLeg,
                    ["leftlowerleg"] = HumanoidBoneId.LeftLowerLeg,
                    ["leftshin"] = HumanoidBoneId.LeftLowerLeg,
                    ["lshin"] = HumanoidBoneId.LeftLowerLeg,
                    ["rightleg"] = HumanoidBoneId.RightLowerLeg,
                    ["rightlowerleg"] = HumanoidBoneId.RightLowerLeg,
                    ["rightshin"] = HumanoidBoneId.RightLowerLeg,
                    ["rshin"] = HumanoidBoneId.RightLowerLeg,

                    ["leftfoot"] = HumanoidBoneId.LeftFoot,
                    ["lfoot"] = HumanoidBoneId.LeftFoot,
                    ["rightfoot"] = HumanoidBoneId.RightFoot,
                    ["rfoot"] = HumanoidBoneId.RightFoot,
                    ["lefttoe"] = HumanoidBoneId.LeftToes,
                    ["lefttoes"] = HumanoidBoneId.LeftToes,
                    ["lefttoebase"] = HumanoidBoneId.LeftToes,
                    ["righttoe"] = HumanoidBoneId.RightToes,
                    ["righttoes"] = HumanoidBoneId.RightToes,
                    ["righttoebase"] = HumanoidBoneId.RightToes,

                    ["spine"] = HumanoidBoneId.Spine,
                    ["spine0"] = HumanoidBoneId.Spine,
                    ["spine1"] = HumanoidBoneId.Chest,
                    ["chest"] = HumanoidBoneId.Chest,
                    ["spine2"] = HumanoidBoneId.UpperChest,
                    ["upperchest"] = HumanoidBoneId.UpperChest,
                    ["spine3"] = HumanoidBoneId.UpperChest,
                    ["neck"] = HumanoidBoneId.Neck,
                    ["neck1"] = HumanoidBoneId.Neck,
                    ["head"] = HumanoidBoneId.Head,

                    ["leftshoulder"] = HumanoidBoneId.LeftShoulder,
                    ["lshoulder"] = HumanoidBoneId.LeftShoulder,
                    ["leftclavicle"] = HumanoidBoneId.LeftShoulder,
                    ["lclavicle"] = HumanoidBoneId.LeftShoulder,
                    ["rightshoulder"] = HumanoidBoneId.RightShoulder,
                    ["rshoulder"] = HumanoidBoneId.RightShoulder,
                    ["rightclavicle"] = HumanoidBoneId.RightShoulder,
                    ["rclavicle"] = HumanoidBoneId.RightShoulder,

                    ["leftarm"] = HumanoidBoneId.LeftUpperArm,
                    ["leftupperarm"] = HumanoidBoneId.LeftUpperArm,
                    ["lupperarm"] = HumanoidBoneId.LeftUpperArm,
                    ["rightarm"] = HumanoidBoneId.RightUpperArm,
                    ["rightupperarm"] = HumanoidBoneId.RightUpperArm,
                    ["rupperarm"] = HumanoidBoneId.RightUpperArm,
                    ["leftforearm"] = HumanoidBoneId.LeftLowerArm,
                    ["leftlowerarm"] = HumanoidBoneId.LeftLowerArm,
                    ["lforearm"] = HumanoidBoneId.LeftLowerArm,
                    ["rightforearm"] = HumanoidBoneId.RightLowerArm,
                    ["rightlowerarm"] = HumanoidBoneId.RightLowerArm,
                    ["rforearm"] = HumanoidBoneId.RightLowerArm,
                    ["lefthand"] = HumanoidBoneId.LeftHand,
                    ["lhand"] = HumanoidBoneId.LeftHand,
                    ["righthand"] = HumanoidBoneId.RightHand,
                    ["rhand"] = HumanoidBoneId.RightHand
                };

        public string AdapterId =>
            "bvh";

        public bool SupportsExtension(
            string extension) =>
                string.Equals(
                    extension,
                    ".bvh",
                    StringComparison.OrdinalIgnoreCase);

        public bool TryImport(
            P11ExternalMotionAdapterContext context,
            out P11ExternalMotionAdapterResult result,
            out string error)
        {
            result = null;
            error = null;

            if (context == null ||
                string.IsNullOrWhiteSpace(
                    context.SourceFilePath) ||
                !File.Exists(
                    context.SourceFilePath))
            {
                error =
                    "BVH source file does not exist.";
                return false;
            }

            ParsedBvh bvh;

            try
            {
                bvh =
                    Parse(
                        File.ReadAllText(
                            context.SourceFilePath));
            }
            catch (Exception exception)
            {
                error =
                    "BVH parse failed: " +
                    exception.Message;
                return false;
            }

            if (bvh.FrameCount < 2 ||
                bvh.FrameTime <= 0f)
            {
                error =
                    "BVH requires at least two frames and a positive frame time.";
                return false;
            }

            var options =
                context.Options ??
                new P11ExternalMotionImportOptions();

            if (!TryBuildCue(
                    bvh,
                    Path.GetFileNameWithoutExtension(
                        context.SourceFilePath),
                    context.MarkerFile,
                    options.BvhPositionScale,
                    options.BvhMirrorX,
                    out var cue,
                    out var mappedBoneCount,
                    out var importedMarkerCount,
                    out error))
            {
                return false;
            }

            var asset =
                ScriptableObject.CreateInstance<
                    BakedMotionCueAsset>();
            asset.SetCue(
                cue);

            var outputPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    context.DestinationAssetFolder +
                    "/" +
                    SanitizeFileName(
                        cue.CueId) +
                    "__BVH.asset");

            try
            {
                AssetDatabase.CreateAsset(
                    asset,
                    outputPath);
                context.CreatedAssetPaths?.Add(
                    outputPath);
                EditorUtility.SetDirty(
                    asset);
                AssetDatabase.SaveAssets();

                result =
                    new P11ExternalMotionAdapterResult
                    {
                        AdapterId =
                            AdapterId,
                        SourceAssetPath =
                            context.SourceAssetPath,
                        CueAssets =
                            new[]
                            {
                                asset
                            },
                        CueAssetPaths =
                            new[]
                            {
                                outputPath
                            },
                        ImportedMarkerCount =
                            importedMarkerCount
                    };

                return true;
            }
            catch (Exception exception)
            {
                AssetDatabase.DeleteAsset(
                    outputPath);

                if (asset != null)
                {
                    UnityEngine.Object
                        .DestroyImmediate(
                            asset);
                }

                error =
                    $"BVH cue asset creation failed after mapping {mappedBoneCount} humanoid bones: {exception.Message}";
                return false;
            }
        }

        private static ParsedBvh Parse(
            string text)
        {
            var reader =
                new TokenReader(
                    text);
            reader.Expect(
                "HIERARCHY");
            reader.Expect(
                "ROOT");

            var joints =
                new List<Joint>();
            var channelCount = 0;
            var root =
                ParseJoint(
                    reader,
                    reader.Read(),
                    joints,
                    ref channelCount);

            reader.Expect(
                "MOTION");
            var framesLabel =
                reader.Read();

            if (!string.Equals(
                    framesLabel,
                    "Frames:",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    framesLabel,
                    "Frames",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"Expected BVH Frames label but found '{framesLabel}'.");
            }

            if (string.Equals(
                    framesLabel,
                    "Frames",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    reader.Peek(),
                    ":",
                    StringComparison.Ordinal))
            {
                reader.Read();
            }

            var frameCount =
                reader.ReadInt();

            reader.Expect(
                "Frame");
            var timeLabel =
                reader.Read();

            if (!string.Equals(
                    timeLabel,
                    "Time:",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    timeLabel,
                    "Time",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"Expected BVH Frame Time label but found '{timeLabel}'.");
            }

            if (string.Equals(
                    timeLabel,
                    "Time",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    reader.Peek(),
                    ":",
                    StringComparison.Ordinal))
            {
                reader.Read();
            }

            var frameTime =
                reader.ReadFloat();

            if (frameCount <= 0 ||
                channelCount <= 0)
            {
                throw new FormatException(
                    "BVH contains no motion frames or channels.");
            }

            var frames =
                new float[
                    frameCount][];

            for (var frame = 0;
                 frame < frameCount;
                 frame++)
            {
                frames[frame] =
                    new float[
                        channelCount];

                for (var channel = 0;
                     channel < channelCount;
                     channel++)
                {
                    frames[frame][
                        channel] =
                            reader.ReadFloat();
                }
            }

            if (!reader.End)
            {
                throw new FormatException(
                    $"BVH contains unexpected trailing token '{reader.Peek()}'.");
            }

            return new ParsedBvh
            {
                Root =
                    root,
                Joints =
                    joints.ToArray(),
                ChannelCount =
                    channelCount,
                FrameCount =
                    frameCount,
                FrameTime =
                    frameTime,
                Frames =
                    frames
            };
        }

        private static Joint ParseJoint(
            TokenReader reader,
            string name,
            ICollection<Joint> joints,
            ref int channelCount)
        {
            var joint =
                new Joint
                {
                    Name =
                        name
                };

            joints.Add(
                joint);
            reader.Expect(
                "{");

            while (true)
            {
                var token =
                    reader.Read();

                if (token == "}")
                {
                    break;
                }

                if (string.Equals(
                        token,
                        "OFFSET",
                        StringComparison.OrdinalIgnoreCase))
                {
                    reader.ReadFloat();
                    reader.ReadFloat();
                    reader.ReadFloat();
                    continue;
                }

                if (string.Equals(
                        token,
                        "CHANNELS",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var count =
                        reader.ReadInt();

                    if (count < 0 ||
                        count > 6)
                    {
                        throw new FormatException(
                            $"BVH joint '{name}' has invalid channel count {count}.");
                    }

                    joint.ChannelStart =
                        channelCount;
                    joint.Channels =
                        new string[
                            count];

                    for (var i = 0;
                         i < count;
                         i++)
                    {
                        var channel =
                            reader.Read();

                        if (!IsSupportedChannel(
                                channel))
                        {
                            throw new FormatException(
                                $"BVH joint '{name}' uses unsupported channel '{channel}'.");
                        }

                        joint.Channels[i] =
                            channel;
                    }

                    channelCount +=
                        count;
                    continue;
                }

                if (string.Equals(
                        token,
                        "JOINT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ParseJoint(
                        reader,
                        reader.Read(),
                        joints,
                        ref channelCount);
                    continue;
                }

                if (string.Equals(
                        token,
                        "End",
                        StringComparison.OrdinalIgnoreCase))
                {
                    reader.Expect(
                        "Site");
                    reader.Expect(
                        "{");
                    reader.Expect(
                        "OFFSET");
                    reader.ReadFloat();
                    reader.ReadFloat();
                    reader.ReadFloat();
                    reader.Expect(
                        "}");
                    continue;
                }

                throw new FormatException(
                    $"Unsupported BVH hierarchy token '{token}' in joint '{name}'.");
            }

            return joint;
        }

        private static bool TryBuildCue(
            ParsedBvh bvh,
            string cueId,
            P11ExternalMotionMarkerFile markerFile,
            float positionScale,
            bool mirrorX,
            out BakedMotionCueDefinition cue,
            out int mappedBoneCount,
            out int importedMarkerCount,
            out string error)
        {
            cue = null;
            mappedBoneCount = 0;
            importedMarkerCount = 0;
            error = null;

            if (float.IsNaN(
                    positionScale) ||
                float.IsInfinity(
                    positionScale) ||
                positionScale <= 0f)
            {
                error =
                    "BVH position scale must be finite and positive.";
                return false;
            }

            var mapped =
                BuildHumanoidMapping(
                    bvh.Joints);

            if (!mapped.ContainsKey(
                    HumanoidBoneId.Hips))
            {
                error =
                    "BVH import requires a root/hips joint that maps to HumanoidBoneId.Hips.";
                return false;
            }

            mappedBoneCount =
                mapped.Count;

            var duration =
                (bvh.FrameCount - 1) *
                bvh.FrameTime;
            BakedMotionCueMarker[] markers =
                Array.Empty<BakedMotionCueMarker>();

            if (markerFile != null)
            {
                if (!P11ExternalMotionImportUtility
                    .TryResolveMarkers(
                        markerFile,
                        cueId,
                        duration,
                        out markers,
                        out error))
                {
                    return false;
                }

                importedMarkerCount =
                    markers.Length;
            }

            var rootPositions =
                new Vector3[
                    bvh.FrameCount];
            var rootRotations =
                new Quaternion[
                    bvh.FrameCount];

            var tracks =
                new List<
                    BakedBoneMotionCueTrack>(
                    mapped.Count);
            var trackByBone =
                new Dictionary<
                    HumanoidBoneId,
                    BakedBoneMotionCueTrack>();

            var mappedPairs =
                new List<
                    KeyValuePair<
                        HumanoidBoneId,
                        Joint>>(
                    mapped);
            mappedPairs.Sort(
                (left, right) =>
                    ((int)left.Key).CompareTo(
                        (int)right.Key));

            foreach (var pair in
                     mappedPairs)
            {
                if (ReferenceEquals(
                        pair.Value,
                        bvh.Root))
                {
                    continue;
                }

                var track =
                    new BakedBoneMotionCueTrack
                    {
                        Bone =
                            pair.Key,
                        LocalPositionOffsets =
                            new Vector3[
                                bvh.FrameCount],
                        LocalRotationOffsets =
                            new Quaternion[
                                bvh.FrameCount]
                    };

                tracks.Add(
                    track);
                trackByBone.Add(
                    pair.Key,
                    track);
            }

            var baseRootPosition =
                ReadPosition(
                    bvh.Root,
                    bvh.Frames[0]) *
                positionScale;
            var baseRootRotation =
                ReadRotation(
                    bvh.Root,
                    bvh.Frames[0]);
            var basePositions =
                new Dictionary<
                    HumanoidBoneId,
                    Vector3>();
            var baseRotations =
                new Dictionary<
                    HumanoidBoneId,
                    Quaternion>();

            foreach (var pair in
                     mappedPairs)
            {
                basePositions[
                    pair.Key] =
                        ReadPosition(
                            pair.Value,
                            bvh.Frames[0]) *
                        positionScale;
                baseRotations[
                    pair.Key] =
                        ReadRotation(
                            pair.Value,
                            bvh.Frames[0]);
            }

            for (var frame = 0;
                 frame < bvh.FrameCount;
                 frame++)
            {
                rootPositions[
                    frame] =
                        ConvertPosition(
                            ReadPosition(
                                bvh.Root,
                                bvh.Frames[
                                    frame]) *
                            positionScale -
                            baseRootPosition,
                            mirrorX);
                rootRotations[
                    frame] =
                        ConvertRotation(
                            Quaternion.Inverse(
                                baseRootRotation) *
                            ReadRotation(
                                bvh.Root,
                                bvh.Frames[
                                    frame]),
                            mirrorX);

                foreach (var pair in
                         mappedPairs)
                {
                    var bone =
                        pair.Key;
                    var joint =
                        pair.Value;
                    var currentPosition =
                        ReadPosition(
                            joint,
                            bvh.Frames[
                                frame]) *
                        positionScale;
                    var currentRotation =
                        ReadRotation(
                            joint,
                            bvh.Frames[
                                frame]);
                    if (!trackByBone.TryGetValue(
                            bone,
                            out var track))
                    {
                        continue;
                    }

                    track.LocalPositionOffsets[
                        frame] =
                            ConvertPosition(
                                currentPosition -
                                basePositions[
                                    bone],
                                mirrorX);
                    track.LocalRotationOffsets[
                        frame] =
                            ConvertRotation(
                                Quaternion.Inverse(
                                    baseRotations[
                                        bone]) *
                                currentRotation,
                                mirrorX);
                }
            }

            cue =
                new BakedMotionCueDefinition
                {
                    CueId =
                        string.IsNullOrWhiteSpace(
                            cueId)
                            ? "bvh-motion"
                            : cueId.Trim(),
                    DurationSeconds =
                        duration,
                    Loop =
                        false,
                    HoldLastPose =
                        false,
                    Markers =
                        markers,
                    PoseSpace =
                        HumanoidPoseSpace
                            .NormalizedLocal,
                    FrameCount =
                        bvh.FrameCount,
                    RootPositionOffsets =
                        rootPositions,
                    RootRotationOffsets =
                        rootRotations,
                    Bones =
                        tracks.ToArray()
                };

            return true;
        }

        private static Vector3 ReadPosition(
            Joint joint,
            float[] frame)
        {
            var result =
                Vector3.zero;

            for (var i = 0;
                 i < joint.Channels.Length;
                 i++)
            {
                var value =
                    frame[
                        joint.ChannelStart +
                        i];

                switch (joint.Channels[i]
                            .ToLowerInvariant())
                {
                    case "xposition":
                        result.x =
                            value;
                        break;
                    case "yposition":
                        result.y =
                            value;
                        break;
                    case "zposition":
                        result.z =
                            value;
                        break;
                }
            }

            return result;
        }

        private static Quaternion ReadRotation(
            Joint joint,
            float[] frame)
        {
            var result =
                Quaternion.identity;

            for (var i = 0;
                 i < joint.Channels.Length;
                 i++)
            {
                var value =
                    frame[
                        joint.ChannelStart +
                        i];

                switch (joint.Channels[i]
                            .ToLowerInvariant())
                {
                    case "xrotation":
                        result *=
                            Quaternion.AngleAxis(
                                value,
                                Vector3.right);
                        break;
                    case "yrotation":
                        result *=
                            Quaternion.AngleAxis(
                                value,
                                Vector3.up);
                        break;
                    case "zrotation":
                        result *=
                            Quaternion.AngleAxis(
                                value,
                                Vector3.forward);
                        break;
                }
            }

            return result;
        }

        private static Vector3 ConvertPosition(
            Vector3 value,
            bool mirrorX) =>
                mirrorX
                    ? new Vector3(
                        -value.x,
                        value.y,
                        value.z)
                    : value;

        private static Quaternion ConvertRotation(
            Quaternion value,
            bool mirrorX) =>
                mirrorX
                    ? new Quaternion(
                        value.x,
                        -value.y,
                        -value.z,
                        value.w)
                    : value;

        private static bool IsSupportedChannel(
            string value)
        {
            switch (value?.ToLowerInvariant())
            {
                case "xposition":
                case "yposition":
                case "zposition":
                case "xrotation":
                case "yrotation":
                case "zrotation":
                    return true;
                default:
                    return false;
            }
        }

        private static Dictionary<
            HumanoidBoneId,
            Joint> BuildHumanoidMapping(
            IEnumerable<Joint> joints)
        {
            var selected =
                new Dictionary<
                    HumanoidBoneId,
                    (Joint Joint, int Priority)>();

            foreach (var joint in
                     joints ??
                     Array.Empty<Joint>())
            {
                if (joint == null ||
                    !TryMapBone(
                        joint.Name,
                        out var bone))
                {
                    continue;
                }

                var normalized =
                    NormalizeJointName(
                        joint.Name);
                var priority =
                    GetMappingPriority(
                        normalized,
                        bone);

                if (!selected.TryGetValue(
                        bone,
                        out var current) ||
                    priority >
                    current.Priority)
                {
                    selected[
                        bone] =
                            (joint, priority);
                }
            }

            var result =
                new Dictionary<
                    HumanoidBoneId,
                    Joint>();

            foreach (var pair in
                     selected)
            {
                result.Add(
                    pair.Key,
                    pair.Value.Joint);
            }

            return result;
        }

        private static int GetMappingPriority(
            string normalized,
            HumanoidBoneId bone)
        {
            if (string.Equals(
                    normalized,
                    NormalizeJointName(
                        bone.ToString()),
                    StringComparison.Ordinal))
            {
                return 100;
            }

            switch (normalized)
            {
                case "hips":
                case "pelvis":
                case "chest":
                case "upperchest":
                case "neck":
                case "head":
                case "leftupperarm":
                case "rightupperarm":
                case "leftlowerarm":
                case "rightlowerarm":
                case "lefthand":
                case "righthand":
                case "leftupperleg":
                case "rightupperleg":
                case "leftlowerleg":
                case "rightlowerleg":
                case "leftfoot":
                case "rightfoot":
                    return 90;

                case "root":
                    return 20;

                default:
                    return 50;
            }
        }

        private static bool TryMapBone(
            string jointName,
            out HumanoidBoneId bone)
        {
            var normalized =
                NormalizeJointName(
                    jointName);

            return BoneAliases.TryGetValue(
                normalized,
                out bone);
        }

        private static string NormalizeJointName(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return string.Empty;
            }

            var separator =
                Math.Max(
                    value.LastIndexOf(':'),
                    value.LastIndexOf('|'));

            if (separator >= 0 &&
                separator <
                value.Length - 1)
            {
                value =
                    value.Substring(
                        separator + 1);
            }

            var builder =
                new System.Text
                    .StringBuilder();

            foreach (var ch in
                     value)
            {
                if (char.IsLetterOrDigit(
                        ch))
                {
                    builder.Append(
                        char.ToLowerInvariant(
                            ch));
                }
            }

            var normalized =
                builder.ToString();

            if (normalized.StartsWith(
                    "mixamorig",
                    StringComparison.Ordinal))
            {
                normalized =
                    normalized.Substring(
                        "mixamorig".Length);
            }

            return normalized;
        }

        private static string SanitizeFileName(
            string value)
        {
            value =
                string.IsNullOrWhiteSpace(
                    value)
                    ? "BvhMotion"
                    : value.Trim();

            foreach (var invalid in
                     Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        invalid,
                        '_');
            }

            return value;
        }
    }
}
