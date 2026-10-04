using System;
using UnityEngine;

namespace VCR.Editor.P12
{
    [CreateAssetMenu(
        menuName = "VCR/P12/Effect Preset",
        fileName = "VCR Effect Preset")]
    public sealed class P12EffectPresetAsset :
        ScriptableObject
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion =
            CurrentFormatVersion;
        public string EffectId;
        public GameObject Prefab;
        public bool RestartOnPlay = true;
        public bool DeactivateOnStop = true;
        public bool StartInactive = true;
    }
}
