using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VCR.Runtime.Tracking.MediaPipe;

namespace VCR.Editor.P0
{
    public static class P0TrackingSceneBuilder
    {
        private const string SceneDirectory = "Assets/VCR/P0";
        private const string ScenePath = SceneDirectory + "/P0Tracking.unity";

        [MenuItem("VCR/P0/Create Tracking Test Scene")]
        public static void CreateTrackingTestScene()
        {
            Directory.CreateDirectory(SceneDirectory);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var trackingRoot = new GameObject("P0 Tracking");
            trackingRoot.AddComponent<MediaPipeWebcamTrackingRunner>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Object.DestroyImmediate(trackingRoot);
                Debug.LogError($"VCR P0: failed to save tracking test scene at {ScenePath}");
                return;
            }

            Selection.activeGameObject = trackingRoot;
            EditorGUIUtility.PingObject(trackingRoot);

            Debug.Log(
                $"VCR P0 tracking scene created: {ScenePath}. " +
                "Press Play and inspect Console tracking/5s diagnostics.");
        }
    }
}
