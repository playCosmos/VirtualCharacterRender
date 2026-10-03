using System;
using UnityEngine;

namespace VCR.Runtime.Environment.Unity
{
    [Serializable]
    public sealed class EnvironmentStateBinding
    {
        [SerializeField] private string stateId = "default";
        [SerializeField] private GameObject root;

        public string StateId => stateId;
        public GameObject Root => root;

        public void Configure(
            string id,
            GameObject stateRoot)
        {
            stateId = id;
            root = stateRoot;
        }
    }
}
