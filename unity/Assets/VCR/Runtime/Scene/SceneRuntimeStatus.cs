namespace VCR.Runtime.Scene
{
    public readonly struct SceneRuntimeStatus
    {
        public SceneRuntimeStatus(
            SceneRuntimeState state,
            bool hasCharacter,
            string currentCharacterPath,
            string lastError,
            int operationGeneration)
        {
            State = state;
            HasCharacter = hasCharacter;
            CurrentCharacterPath = currentCharacterPath;
            LastError = lastError;
            OperationGeneration = operationGeneration;
        }

        public SceneRuntimeState State { get; }
        public bool HasCharacter { get; }
        public string CurrentCharacterPath { get; }
        public string LastError { get; }
        public int OperationGeneration { get; }
    }
}
