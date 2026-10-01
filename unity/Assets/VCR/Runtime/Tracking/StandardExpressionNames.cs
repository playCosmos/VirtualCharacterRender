using System;
using System.Collections.Generic;

namespace VCR.Runtime.Tracking
{
    public static class StandardExpressionNames
    {
        private static readonly Dictionary<string, StandardExpression> Lookup =
            BuildLookup();

        public static bool TryParse(
            string name,
            out StandardExpression expression)
        {
            if (string.IsNullOrEmpty(name))
            {
                expression = default;
                return false;
            }

            return Lookup.TryGetValue(name, out expression);
        }

        public static string GetVrm1Name(StandardExpression expression)
        {
            return expression switch
            {
                StandardExpression.Neutral => "neutral",
                StandardExpression.Happy => "happy",
                StandardExpression.Angry => "angry",
                StandardExpression.Sad => "sad",
                StandardExpression.Relaxed => "relaxed",
                StandardExpression.Surprised => "surprised",
                StandardExpression.Aa => "aa",
                StandardExpression.Ih => "ih",
                StandardExpression.Ou => "ou",
                StandardExpression.Ee => "ee",
                StandardExpression.Oh => "oh",
                StandardExpression.Blink => "blink",
                StandardExpression.BlinkLeft => "blinkLeft",
                StandardExpression.BlinkRight => "blinkRight",
                StandardExpression.LookUp => "lookUp",
                StandardExpression.LookDown => "lookDown",
                StandardExpression.LookLeft => "lookLeft",
                StandardExpression.LookRight => "lookRight",
                _ => string.Empty
            };
        }

        public static string GetVmcVrm0Name(StandardExpression expression)
        {
            return expression switch
            {
                StandardExpression.Neutral => "Neutral",
                StandardExpression.Happy => "Joy",
                StandardExpression.Angry => "Angry",
                StandardExpression.Sad => "Sorrow",
                StandardExpression.Relaxed => "Fun",
                StandardExpression.Aa => "A",
                StandardExpression.Ih => "I",
                StandardExpression.Ou => "U",
                StandardExpression.Ee => "E",
                StandardExpression.Oh => "O",
                StandardExpression.Blink => "Blink",
                StandardExpression.BlinkLeft => "Blink_L",
                StandardExpression.BlinkRight => "Blink_R",
                StandardExpression.LookUp => "LookUp",
                StandardExpression.LookDown => "LookDown",
                StandardExpression.LookLeft => "LookLeft",
                StandardExpression.LookRight => "LookRight",
                _ => string.Empty
            };
        }

        private static Dictionary<string, StandardExpression> BuildLookup()
        {
            var lookup = new Dictionary<string, StandardExpression>(
                StringComparer.OrdinalIgnoreCase);

            Add(lookup, StandardExpression.Neutral, "Neutral", "neutral");
            Add(lookup, StandardExpression.Happy, "Joy", "happy");
            Add(lookup, StandardExpression.Angry, "Angry", "angry");
            Add(lookup, StandardExpression.Sad, "Sorrow", "sad");
            Add(lookup, StandardExpression.Relaxed, "Fun", "relaxed");
            Add(lookup, StandardExpression.Surprised, "Surprised", "surprised");

            Add(lookup, StandardExpression.Aa, "A", "aa");
            Add(lookup, StandardExpression.Ih, "I", "ih");
            Add(lookup, StandardExpression.Ou, "U", "ou");
            Add(lookup, StandardExpression.Ee, "E", "ee");
            Add(lookup, StandardExpression.Oh, "O", "oh");

            Add(lookup, StandardExpression.Blink, "Blink", "blink");
            Add(lookup, StandardExpression.BlinkLeft, "Blink_L", "blinkLeft");
            Add(lookup, StandardExpression.BlinkRight, "Blink_R", "blinkRight");

            Add(lookup, StandardExpression.LookUp, "LookUp", "lookUp");
            Add(lookup, StandardExpression.LookDown, "LookDown", "lookDown");
            Add(lookup, StandardExpression.LookLeft, "LookLeft", "lookLeft");
            Add(lookup, StandardExpression.LookRight, "LookRight", "lookRight");

            return lookup;
        }

        private static void Add(
            Dictionary<string, StandardExpression> lookup,
            StandardExpression expression,
            params string[] names)
        {
            foreach (var name in names)
            {
                lookup[name] = expression;
            }
        }
    }
}
