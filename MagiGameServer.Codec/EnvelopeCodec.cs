using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MagiGameServer.Contracts.Protocol;

namespace MagiGameServer.Codec
{
    /// Single-stop JSON codec for every wire envelope. Two surfaces:
    ///
    /// * Typed (client) API: `Serialize&lt;T&gt;` / `Deserialize&lt;T&gt;` — used
    ///   anywhere TState / TAction are known at compile time. Client
    ///   transports always know what TState their dispatcher is
    ///   parameterised on, so they use this path.
    ///
    /// * Runtime-typed (server) API: `DeserializeActionEnvelope(json,
    ///   actionType)` — used by the server host where the concrete
    ///   TAction is only known after resolving the session's module.
    ///   Internally builds ActionEnvelope&lt;TAction&gt; via reflection and
    ///   hands it off to the same JsonSerializerOptions.
    ///
    /// Options are immutable and shared: enum-as-string,
    /// strong-typed-identifier converters, camelCase property naming.
    /// Shared instance so callers don't pay the ~100ms per-options warm-up
    /// on every call, and so a mismatch between server and client casing
    /// is impossible (there's one set of options, not two).
    public static class EnvelopeCodec
    {
        public static JsonSerializerOptions Options { get; } = BuildOptions();

        private static JsonSerializerOptions BuildOptions()
        {
            var o = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            };
            o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            o.Converters.Add(new SessionIdConverter());
            o.Converters.Add(new SeatIdConverter());
            o.Converters.Add(new ClientSeqConverter());
            o.Converters.Add(new ServerSeqConverter());
            return o;
        }

        public static byte[] Serialize<T>(T value)
            => JsonSerializer.SerializeToUtf8Bytes(value, Options);

        public static string SerializeToString<T>(T value)
            => JsonSerializer.Serialize(value, Options);

        public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json)
            => JsonSerializer.Deserialize<T>(utf8Json, Options);

        public static T Deserialize<T>(string json)
            => JsonSerializer.Deserialize<T>(json, Options);

        /// Decodes an inbound ActionEnvelope for the server side. Returns
        /// ActionEnvelope&lt;object&gt; directly — the exact shape
        /// Session.Apply consumes — rather than ActionEnvelope&lt;TAction&gt;,
        /// because C# generic types are invariant: an
        /// ActionEnvelope&lt;CounterAction&gt; is not assignment-compatible
        /// with ActionEnvelope&lt;object&gt;, so a naive MakeGenericType path
        /// would force the host into reflection-cast glue at the seam.
        ///
        /// Implementation: deserialize into ActionEnvelope&lt;JsonElement&gt;
        /// as an intermediate shape, then deserialize the Action
        /// JsonElement against the runtime `actionType` and box the
        /// result into an ActionEnvelope&lt;object&gt;. The non-generic
        /// IRulesAdapter downstream will cast Action back to its
        /// concrete type inside RulesAdapterBase's object-bridge.
        public static ActionEnvelope<object> DeserializeActionEnvelope(ReadOnlySpan<byte> utf8Json, Type actionType)
        {
            if (actionType == null) throw new ArgumentNullException(nameof(actionType));
            var raw = JsonSerializer.Deserialize<ActionEnvelope<JsonElement>>(utf8Json, Options);
            return BuildObjectEnvelope(raw, actionType);
        }

        public static ActionEnvelope<object> DeserializeActionEnvelope(string json, Type actionType)
        {
            if (actionType == null) throw new ArgumentNullException(nameof(actionType));
            var raw = JsonSerializer.Deserialize<ActionEnvelope<JsonElement>>(json, Options);
            return BuildObjectEnvelope(raw, actionType);
        }

        private static ActionEnvelope<object> BuildObjectEnvelope(ActionEnvelope<JsonElement> raw, Type actionType)
        {
            if (raw == null) return null;
            object action = null;
            if (raw.Action.ValueKind != JsonValueKind.Undefined && raw.Action.ValueKind != JsonValueKind.Null)
            {
                action = JsonSerializer.Deserialize(raw.Action.GetRawText(), actionType, Options);
            }
            return new ActionEnvelope<object>
            {
                Session = raw.Session,
                Seat = raw.Seat,
                Seq = raw.Seq,
                Action = action,
                PredictedStateHash = raw.PredictedStateHash,
            };
        }
    }
}
