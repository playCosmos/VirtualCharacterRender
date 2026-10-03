namespace VCR.Runtime.EventRuntime
{
    public interface IEventActionHandler
    {
        bool CanHandle(
            EventActionCommand command);

        bool TryExecute(
            EventActionCommand command,
            out string error);
    }

    public interface IEventActionCompletionProbe
    {
        bool CanTrackCompletion(
            EventActionCommand command);

        bool TryIsComplete(
            EventActionCommand command,
            out bool complete,
            out string error);
    }
}
