using System;

namespace VCR.Runtime.UI
{
    public enum ApplicationUiSection
    {
        Character = 0,
        Tracking = 1,
        MotionExpression = 2,
        Environment = 3,
        MaterialShader = 4,
        Events = 5,
        CameraOutput = 6,
        Settings = 7,
        Diagnostics = 8,
        Count = 9
    }

    public readonly struct ApplicationUiSectionState
    {
        public ApplicationUiSectionState(
            ApplicationUiSection section,
            string title,
            bool available,
            string unavailableReason)
        {
            Section = section;
            Title = title;
            Available = available;
            UnavailableReason = unavailableReason;
        }

        public ApplicationUiSection Section { get; }
        public string Title { get; }
        public bool Available { get; }
        public string UnavailableReason { get; }
    }

    public sealed class ApplicationUiModel
    {
        private readonly bool[] _available =
            new bool[(int)ApplicationUiSection.Count];

        private readonly string[] _reasons =
            new string[(int)ApplicationUiSection.Count];

        public ApplicationUiModel()
        {
            for (var i = 0; i < _available.Length; i++)
            {
                _available[i] = true;
            }

            SelectedSection =
                ApplicationUiSection.Character;
        }

        public ApplicationUiSection SelectedSection
        {
            get;
            private set;
        }

        public bool TrySelect(
            ApplicationUiSection section)
        {
            if (!IsValid(section) ||
                !IsAvailable(section))
            {
                return false;
            }

            SelectedSection = section;
            return true;
        }

        public void SetAvailability(
            ApplicationUiSection section,
            bool available,
            string unavailableReason = null)
        {
            if (!IsValid(section))
            {
                return;
            }

            var index = (int)section;
            _available[index] = available;
            _reasons[index] =
                available
                    ? null
                    : unavailableReason;

            if (section == SelectedSection &&
                !available)
            {
                SelectFirstAvailable();
            }
        }

        public bool IsAvailable(
            ApplicationUiSection section)
        {
            return
                IsValid(section) &&
                _available[(int)section];
        }

        public string GetUnavailableReason(
            ApplicationUiSection section)
        {
            return
                IsValid(section)
                    ? _reasons[(int)section]
                    : "Unknown UI section.";
        }

        public ApplicationUiSectionState[]
            CaptureSections()
        {
            var result =
                new ApplicationUiSectionState[
                    (int)ApplicationUiSection.Count];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                var section =
                    (ApplicationUiSection)i;

                result[i] =
                    new ApplicationUiSectionState(
                        section,
                        GetTitle(section),
                        _available[i],
                        _reasons[i]);
            }

            return result;
        }

        public static string GetTitle(
            ApplicationUiSection section)
        {
            return section switch
            {
                ApplicationUiSection.Character =>
                    "Character",
                ApplicationUiSection.Tracking =>
                    "Tracking",
                ApplicationUiSection.MotionExpression =>
                    "Motion / Expression",
                ApplicationUiSection.Environment =>
                    "Environment",
                ApplicationUiSection.MaterialShader =>
                    "Material / Shader",
                ApplicationUiSection.Events =>
                    "Events",
                ApplicationUiSection.CameraOutput =>
                    "Camera / Output",
                ApplicationUiSection.Settings =>
                    "Settings",
                ApplicationUiSection.Diagnostics =>
                    "Diagnostics",
                _ =>
                    "Unknown"
            };
        }

        private void SelectFirstAvailable()
        {
            for (var i = 0;
                 i < _available.Length;
                 i++)
            {
                if (_available[i])
                {
                    SelectedSection =
                        (ApplicationUiSection)i;
                    return;
                }
            }
        }

        private static bool IsValid(
            ApplicationUiSection section)
        {
            return
                section >=
                    ApplicationUiSection.Character &&
                section <
                    ApplicationUiSection.Count;
        }
    }
}
