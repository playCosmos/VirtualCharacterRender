namespace VCR.Runtime.Core
{
    public readonly struct RuntimeMetric
    {
        public RuntimeMetric(string name, double value, string unit)
        {
            Name = name;
            Value = value;
            Unit = unit;
        }

        public string Name { get; }
        public double Value { get; }
        public string Unit { get; }
    }
}
