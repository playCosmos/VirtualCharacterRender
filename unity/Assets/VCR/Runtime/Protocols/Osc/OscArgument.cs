namespace VCR.Runtime.Protocols.Osc
{
    public readonly struct OscArgument
    {
        private OscArgument(
            OscArgumentType type,
            int intValue,
            float floatValue,
            string stringValue)
        {
            Type = type;
            IntValue = intValue;
            FloatValue = floatValue;
            StringValue = stringValue;
        }

        public OscArgumentType Type { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public string StringValue { get; }

        public static OscArgument FromInt(int value) =>
            new(OscArgumentType.Int32, value, 0f, null);

        public static OscArgument FromFloat(float value) =>
            new(OscArgumentType.Float32, 0, value, null);

        public static OscArgument FromString(string value) =>
            new(OscArgumentType.String, 0, 0f, value ?? string.Empty);

        public bool TryGetInt(out int value)
        {
            value = IntValue;
            return Type == OscArgumentType.Int32;
        }

        public bool TryGetFloat(out float value)
        {
            value = FloatValue;
            return Type == OscArgumentType.Float32;
        }

        public bool TryGetString(out string value)
        {
            value = StringValue;
            return Type == OscArgumentType.String;
        }
    }
}
