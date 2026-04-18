namespace MagiGameServer.Contracts.Protocol
{
    /// Discriminator for the two client-to-server WebSocket frame shapes.
    /// Carried on ClientFrame&lt;TAction&gt; so the host doesn't have to peek
    /// at optional property presence to figure out which payload a frame
    /// is. Adding a new outbound shape means adding an enum value; the
    /// compiler will flag every switch that doesn't handle it.
    public enum ClientFrameKind
    {
        Action,
        Takeback,
    }

    /// Discriminator for server-to-client WebSocket frames. Mutually exclusive
    /// with itself — exactly one payload field on ServerFrame is non-null
    /// for a given Kind. Kept as a single wrapper rather than five
    /// per-shape messages so the transport needs one read/write pipeline,
    /// not five.
    public enum ServerFrameKind
    {
        JoinSnapshot,
        StateEcho,
        Error,
        TakebackResponse,
        TakebackBroadcast,
    }

    /// Outbound WebSocket wrapper. Clients only ever send one of two things
    /// (submit an action, request a takeback), so a single discriminated
    /// envelope carries both rather than opening two independent wire
    /// shapes. TAction is the module-specific action type; the server
    /// dispatches the wire Kind before resolving it.
    public sealed record ClientFrame<TAction>
    {
        public ClientFrameKind Kind { get; init; }
        public ActionEnvelope<TAction> Action { get; init; }
        public TakebackRequest Takeback { get; init; }
    }

    /// Inbound WebSocket wrapper. Exactly one of the payload fields is
    /// populated; the rest are null. The wrapper exists so the transport
    /// can deserialize a single shape off the socket and the dispatcher
    /// can route by Kind, rather than attempting five parallel
    /// deserialization probes.
    public sealed record ServerFrame<TState>
    {
        public ServerFrameKind Kind { get; init; }
        public JoinSnapshot<TState> JoinSnapshot { get; init; }
        public StateEcho<TState> Echo { get; init; }
        public ErrorEnvelope Error { get; init; }
        public TakebackResponse TakebackResponse { get; init; }
        public TakebackBroadcast<TState> TakebackBroadcast { get; init; }
    }
}
