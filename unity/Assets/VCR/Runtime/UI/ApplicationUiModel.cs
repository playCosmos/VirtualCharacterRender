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
        Expression = 9,
        Appearance = 10,
        Count = 11
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
                    "캐릭터",
                ApplicationUiSection.MotionExpression =>
                    "모션 & 애니메이션",
                ApplicationUiSection.Tracking =>
                    "트래킹",
                ApplicationUiSection.Expression =>
                    "표정",
                ApplicationUiSection.Appearance =>
                    "의상 & 액세서리",
                ApplicationUiSection.Environment =>
                    "배경 & 스테이지",
                ApplicationUiSection.CameraOutput =>
                    "출력",
                ApplicationUiSection.Settings =>
                    "설정",
                ApplicationUiSection.MaterialShader =>
                    "머티리얼 / 셰이더",
                ApplicationUiSection.Events =>
                    "이벤트",
                ApplicationUiSection.Diagnostics =>
                    "진단",
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
