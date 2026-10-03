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
}
