namespace VCR.Runtime.Scene
{
    public enum SceneRuntimeState
    {
        Uninitialized = 0,
        Ready = 1,
        LoadingCharacter = 2,
        CharacterReady = 3,
        Faulted = 4,
        ShuttingDown = 5,
        Stopped = 6
    }
}
