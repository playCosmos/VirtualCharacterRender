using System;

namespace VCR.Runtime.Tracking
{
    [Serializable]
    public readonly struct NamedExpressionValue
    {
        public NamedExpressionValue(string name, float value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public float Value { get; }
    }
}
