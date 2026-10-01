namespace VCR.Runtime.Events
{
    public interface INormalizedEventSink
    {
        void Publish(NormalizedEvent value);
    }
}
