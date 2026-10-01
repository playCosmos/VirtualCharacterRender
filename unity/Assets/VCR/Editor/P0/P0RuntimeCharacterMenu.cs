using System;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Character;

namespace VCR.Editor.P0
{
    public static class P0RuntimeCharacterMenu
    {
        [MenuItem("VCR/P0/Load VRM Into Runtime Scene")]
        public static async void LoadVrm()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError(
                    "VCR P0: enter Play mode before runtime-loading a VRM.");
                return;
            }

            var loader =
                Object.FindFirstObjectByType<
                    Vrm10CharacterLoader>();

            if (loader == null)
            {
                Debug.LogError(
                    "VCR P0: no Vrm10CharacterLoader found. " +
                    "Create the P0 runtime test scene first.");
                return;
            }

            var path =
                EditorUtility.OpenFilePanel(
                    "Load VRM",
                    "",
                    "vrm");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var loaded =
                    await loader.LoadAsync(path);

                if (loaded == null)
                {
                    return;
                }

                Selection.activeGameObject =
                    loaded.gameObject;
                EditorGUIUtility.PingObject(
                    loaded.gameObject);

                Debug.Log(
                    $"VCR P0: loaded '{path}' through the VRM0/VRM1 unified runtime path.",
                    loaded);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"VCR P0 VRM load failed: {exception.Message}");
                Debug.LogException(exception);
            }
        }

        [MenuItem("VCR/P0/Unload Runtime VRM")]
        public static void UnloadVrm()
        {
            var loader =
                Object.FindFirstObjectByType<
                    Vrm10CharacterLoader>();

            if (loader == null)
            {
                return;
            }

            loader.Unload();
            Debug.Log("VCR P0: runtime VRM unloaded.");
        }
    }
}
