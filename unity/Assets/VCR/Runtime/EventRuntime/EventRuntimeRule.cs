using System;

namespace VCR.Runtime.EventRuntime
{
    [Serializable]
    public sealed class EventRuntimeRule
    {
        public string Id;
        public bool Enabled = true;
        public EventRuleFilter Filter = new();
        public EventStateCondition[] Conditions =
            Array.Empty<EventStateCondition>();
        public EventStateMutation[] StateMutations =
            Array.Empty<EventStateMutation>();
        public EventActionTemplate[] Actions =
            Array.Empty<EventActionTemplate>();
        public bool StopAfterMatch;
    }
}
