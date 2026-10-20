namespace VCR.Runtime.Materials
{
    public readonly struct MaterialCompatibilityIssue
    {
        public MaterialCompatibilityIssue(
            string code,
            string propertyName,
            string message)
        {
            Code = code;
            PropertyName = propertyName;
            Message = message;
        }

        public string Code { get; }
        public string PropertyName { get; }
        public string Message { get; }
    }
}
