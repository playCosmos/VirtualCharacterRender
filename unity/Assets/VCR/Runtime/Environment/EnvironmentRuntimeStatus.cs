namespace VCR.Runtime.Environment
{
    public readonly struct EnvironmentRuntimeStatus
    {
        public EnvironmentRuntimeStatus(
            string environmentId,
            string stateId,
            EnvironmentUpdatePolicy updatePolicy,
            bool active,
            string error)
        {
            EnvironmentId = environmentId;
            StateId = stateId;
            UpdatePolicy = updatePolicy;
            Active = active;
            Error = error;
        }

        public string EnvironmentId { get; }
        public string StateId { get; }
        public EnvironmentUpdatePolicy UpdatePolicy { get; }
        public bool Active { get; }
        public string Error { get; }
    }
}
