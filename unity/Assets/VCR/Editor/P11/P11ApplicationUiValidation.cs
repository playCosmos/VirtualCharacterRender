using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VCR.Runtime.UI;

namespace VCR.Editor.P11
{
    public static class P11ApplicationUiValidation
    {
        [MenuItem("VCR/P11/Validate Application UI")]
        public static void Validate()
        {
            RunChecks();
        }

        public static bool RunChecks()
        {
            var failures =
                new List<string>();

            var model =
                new ApplicationUiModel();

            var sections =
                model.CaptureSections();

            Expect(
                sections.Length ==
                    (int)ApplicationUiSection.Count,
                "application UI must expose exactly the roadmap section count",
                failures);

            Expect(
                sections.Length == 9 &&
                sections[0].Section ==
                    ApplicationUiSection.Character &&
                sections[8].Section ==
                    ApplicationUiSection.Diagnostics,
                "application UI section order must remain stable from Character through Diagnostics",
                failures);

            Expect(
                model.SelectedSection ==
                    ApplicationUiSection.Character,
                "application UI must start on Character when it is available",
                failures);

            model.SetAvailability(
                ApplicationUiSection.Character,
                false,
                "character unavailable");

            Expect(
                model.SelectedSection ==
                    ApplicationUiSection.Tracking,
                "disabling the selected section must select the first available section",
                failures);

            model.SetAvailability(
                ApplicationUiSection.Tracking,
                false,
                "tracking unavailable");

            Expect(
                !model.TrySelect(
                    ApplicationUiSection.Tracking) &&
                model.SelectedSection ==
                    ApplicationUiSection.MotionExpression,
                "unavailable sections must reject navigation and preserve a valid selection",
                failures);

            model.SetAvailability(
                ApplicationUiSection.Diagnostics,
                true);

            Expect(
                model.TrySelect(
                    ApplicationUiSection.Diagnostics) &&
                model.SelectedSection ==
                    ApplicationUiSection.Diagnostics,
                "available sections must be selectable",
                failures);

            Expect(
                ApplicationUiModel.GetTitle(
                    ApplicationUiSection
                        .MotionExpression) ==
                    "Motion / Expression" &&
                ApplicationUiModel.GetTitle(
                    ApplicationUiSection
                        .CameraOutput) ==
                    "Camera / Output",
                "section titles must use stable user-facing labels",
                failures);

            var unavailable =
                model.CaptureSections();

            Expect(
                !unavailable[
                    (int)ApplicationUiSection
                        .Tracking].Available &&
                unavailable[
                    (int)ApplicationUiSection
                        .Tracking]
                    .UnavailableReason ==
                    "tracking unavailable",
                "section snapshots must preserve explicit unavailable reasons",
                failures);

            if (failures.Count == 0)
            {
                Debug.Log(
                    "VCR P11 application UI validation: PASS " +
                    "(section order, availability, fallback selection, stable labels)");
                return true;
            }

            Debug.LogError(
                "VCR P11 application UI validation: FAIL\n" +
                string.Join(
                    "\n",
                    failures));
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
}
