using System;

namespace MagiGameServer.Contracts.Core
{
    /// Opaque handle for a live match on the server. Issued by the server at
    /// session creation and echoed back on every action/state envelope so the
    /// server can multiplex concurrent matches on a single transport.
    /// Wrapped as a readonly struct so seat-vs-session mix-ups are caught at
    /// the type system rather than by runtime `if (id == other.id)` checks.
    public readonly struct SessionId : IEquatable<SessionId>
    {
        public string Value { get; }
        public SessionId(string value) { Value = value; }
        public bool Equals(SessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SessionId other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? "<empty>";
        public static bool operator ==(SessionId a, SessionId b) => a.Equals(b);
        public static bool operator !=(SessionId a, SessionId b) => !a.Equals(b);
    }

    /// Identifies a player slot within a session. Distinct from any client's
    /// connection handle: the same human may reconnect to the same seat from a
    /// new socket. Seat 0 is valid — slot indices are 0-based.
    public readonly struct SeatId : IEquatable<SeatId>
    {
        public int Value { get; }
        public SeatId(int value) { Value = value; }
        public bool Equals(SeatId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SeatId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"seat:{Value}";
        public static bool operator ==(SeatId a, SeatId b) => a.Equals(b);
        public static bool operator !=(SeatId a, SeatId b) => !a.Equals(b);
    }

    /// Monotonically increasing per-client action counter. The client stamps
    /// every submitted action with the next sequence number; the server echoes
    /// it back so the client can match echoes to pending optimistic actions.
    /// `long` rather than `int` because a bot session pounding at 60 Hz for a
    /// week still fits, and the extra bytes on the wire are cheap relative to
    /// the pain of a sequence rollover during a live session.
    public readonly struct ClientSeq : IEquatable<ClientSeq>, IComparable<ClientSeq>
    {
        public long Value { get; }
        public ClientSeq(long value) { Value = value; }
        public ClientSeq Next() => new ClientSeq(Value + 1);
        public bool Equals(ClientSeq other) => Value == other.Value;
        public int CompareTo(ClientSeq other) => Value.CompareTo(other.Value);
        public override bool Equals(object obj) => obj is ClientSeq other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"seq:{Value}";
        public static bool operator ==(ClientSeq a, ClientSeq b) => a.Equals(b);
        public static bool operator !=(ClientSeq a, ClientSeq b) => !a.Equals(b);
        public static bool operator <(ClientSeq a, ClientSeq b) => a.Value < b.Value;
        public static bool operator >(ClientSeq a, ClientSeq b) => a.Value > b.Value;
        public static bool operator <=(ClientSeq a, ClientSeq b) => a.Value <= b.Value;
        public static bool operator >=(ClientSeq a, ClientSeq b) => a.Value >= b.Value;
    }

    /// Server-stamped, globally monotonic revision number. Unlike ClientSeq —
    /// which is per-client and only disambiguates within one submitter's own
    /// optimistic stack — ServerSeq is authoritative across every seat in the
    /// session and names a single position on the canonical timeline. Every
    /// accepted action advances the revision by one; every takeback rewinds it.
    /// Echoes broadcast to non-submitting seats use this field as the ordering
    /// key, since AckedSeq is meaningless to anyone but the submitter.
    public readonly struct ServerSeq : IEquatable<ServerSeq>, IComparable<ServerSeq>
    {
        public long Value { get; }
        public ServerSeq(long value) { Value = value; }
        public ServerSeq Next() => new ServerSeq(Value + 1);
        public bool Equals(ServerSeq other) => Value == other.Value;
        public int CompareTo(ServerSeq other) => Value.CompareTo(other.Value);
        public override bool Equals(object obj) => obj is ServerSeq other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"rev:{Value}";
        public static bool operator ==(ServerSeq a, ServerSeq b) => a.Equals(b);
        public static bool operator !=(ServerSeq a, ServerSeq b) => !a.Equals(b);
        public static bool operator <(ServerSeq a, ServerSeq b) => a.Value < b.Value;
        public static bool operator >(ServerSeq a, ServerSeq b) => a.Value > b.Value;
        public static bool operator <=(ServerSeq a, ServerSeq b) => a.Value <= b.Value;
        public static bool operator >=(ServerSeq a, ServerSeq b) => a.Value >= b.Value;
    }

    /// Outcome of applying an action on the server. `Applied` means the action
    /// was valid and the state advanced. `Rejected` means the rules refused
    /// (e.g. illegal move). `Desynced` means the client's predicted state
    /// hash didn't match the server's post-apply hash — the client must replay
    /// from the echoed canonical state instead of reconciling forward.
    public enum ApplyOutcome
    {
        Applied,
        Rejected,
        Desynced
    }
}
