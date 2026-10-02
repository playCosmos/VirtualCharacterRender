namespace VCR.Runtime.Tracking.ArKitUnity
{
    public readonly struct ArKitReceiverStatus
    {
        public ArKitReceiverStatus(
            ArKitReceiverLifecycleState state,
            string remoteAddress,
            int remotePort,
            int localPort,
            long datagramCount,
            long parsedFrameCount,
            long rejectedSenderCount,
            long parseFailureCount,
            long handshakeCount,
            long socketErrorCount,
            string error)
        {
            State = state;
            RemoteAddress = remoteAddress;
            RemotePort = remotePort;
            LocalPort = localPort;
            DatagramCount = datagramCount;
            ParsedFrameCount = parsedFrameCount;
            RejectedSenderCount = rejectedSenderCount;
            ParseFailureCount = parseFailureCount;
            HandshakeCount = handshakeCount;
            SocketErrorCount = socketErrorCount;
            Error = error;
        }

        public ArKitReceiverLifecycleState State { get; }
        public string RemoteAddress { get; }
        public int RemotePort { get; }
        public int LocalPort { get; }
        public long DatagramCount { get; }
        public long ParsedFrameCount { get; }
        public long RejectedSenderCount { get; }
        public long ParseFailureCount { get; }
        public long HandshakeCount { get; }
        public long SocketErrorCount { get; }
        public string Error { get; }

        public bool IsRunning =>
            State == ArKitReceiverLifecycleState.Running ||
            State == ArKitReceiverLifecycleState.SourceLost;
    }
}
