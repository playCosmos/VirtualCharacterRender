using System;
using UnityEngine;

namespace VCR.Runtime.Environment.Unity
{
    [Serializable]
    public sealed class EnvironmentCanvasGroupBinding
    {
        [SerializeField] private string stateId = "default";
        [SerializeField] private CanvasGroup canvasGroup;

        public string StateId => stateId;
        public CanvasGroup CanvasGroup => canvasGroup;

        public void Configure(
            string id,
            CanvasGroup group)
        {
            stateId = id;
            canvasGroup = group;
        }
    }
}
