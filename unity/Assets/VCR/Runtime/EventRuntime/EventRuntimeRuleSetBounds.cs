using System;

namespace VCR.Runtime.EventRuntime
{
    public static class EventRuntimeRuleSetBounds
    {
        public const int MaxRules = 1024;
        public const int MaxConditionsPerRule = 64;
        public const int MaxMutationsPerRule = 64;
        public const int MaxActionsPerRule = 64;

        public static bool TryValidate(
            EventRuntimeRule[] rules,
            out string error)
        {
            error = null;

            if (rules == null)
            {
                return true;
            }

            if (rules.Length >
                MaxRules)
            {
                error =
                    $"Event rule set contains {rules.Length} rules; limit is {MaxRules}.";
                return false;
            }

            for (var i = 0;
                 i < rules.Length;
                 i++)
            {
                var rule =
                    rules[i];

                if (rule == null)
                {
                    continue;
                }

                if ((rule.Conditions?.Length ?? 0) >
                    MaxConditionsPerRule)
                {
                    error =
                        $"Event rule '{rule.Id ?? "<unnamed>"}' contains too many conditions; limit is {MaxConditionsPerRule}.";
                    return false;
                }

                if ((rule.StateMutations?.Length ?? 0) >
                    MaxMutationsPerRule)
                {
                    error =
                        $"Event rule '{rule.Id ?? "<unnamed>"}' contains too many state mutations; limit is {MaxMutationsPerRule}.";
                    return false;
                }

                if ((rule.Actions?.Length ?? 0) >
                    MaxActionsPerRule)
                {
                    error =
                        $"Event rule '{rule.Id ?? "<unnamed>"}' contains too many actions; limit is {MaxActionsPerRule}.";
                    return false;
                }
            }

            return true;
        }
    }
}
