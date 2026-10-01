using UniVRM10;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.Character;

namespace VCR.Editor.P0
{
    public static class P0VrmTrackingTargetMenu
    {
        [MenuItem("VCR/P0/Attach Tracking Target to Selected VRM", true)]
        private static bool ValidateAttach()
        {
            return FindSelectedVrm() != null;
        }

        [MenuItem("VCR/P0/Attach Tracking Target to Selected VRM")]
        private static void Attach()
        {
            var vrm = FindSelectedVrm();
            if (vrm == null)
            {
                Debug.LogError("VCR P0: select a GameObject inside a Vrm10Instance.");
                return;
            }

            var target = vrm.GetComponent<Vrm10TrackingTarget>();
            if (target == null)
            {
                target = Undo.AddComponent<Vrm10TrackingTarget>(vrm.gameObject);
            }

            var fullBodyTarget = vrm.GetComponent<Vrm10HumanoidPoseTarget>();
            if (fullBodyTarget == null)
            {
                fullBodyTarget = Undo.AddComponent<Vrm10HumanoidPoseTarget>(vrm.gameObject);
            }

            var snapshotProvider = vrm.GetComponent<Vrm10MotionSnapshotProvider>();
            if (snapshotProvider == null)
            {
                snapshotProvider = Undo.AddComponent<Vrm10MotionSnapshotProvider>(vrm.gameObject);
            }

            Selection.activeGameObject = vrm.gameObject;
            EditorGUIUtility.PingObject(target);

            Debug.Log(
                "VCR P0: tracking targets attached. " +
                "Face/upper-body, optional full-body VMC, and normalized VMC output snapshot are ready. " +
                "Targets prefer a routed ITrackingFrameProvider when present.",
                target);
        }

        private static Vrm10Instance FindSelectedVrm()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                return null;
            }

            return selected.GetComponentInParent<Vrm10Instance>() ??
                   selected.GetComponentInChildren<Vrm10Instance>();
        }
    }
}
