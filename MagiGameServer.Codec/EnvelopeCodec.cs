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

        /// Builds ActionEnvelope&lt;actionType&gt; at runtime and deserializes
        /// into it. Returns object because the caller's static view of
        /// TAction is "unknown until module lookup" — the server's
        /// Session receives it as ActionEnvelope&lt;object&gt; anyway, since
        /// IRulesAdapter.Apply takes object. Callers that want the typed
        /// envelope cast it themselves.
        public static object DeserializeActionEnvelope(ReadOnlySpan<byte> utf8Json, Type actionType)
        {
            if (actionType == null) throw new ArgumentNullException(nameof(actionType));
            var envelopeType = typeof(ActionEnvelope<>).MakeGenericType(actionType);
            return JsonSerializer.Deserialize(utf8Json, envelopeType, Options);
        }

        public static object DeserializeActionEnvelope(string json, Type actionType)
        {
            if (actionType == null) throw new ArgumentNullException(nameof(actionType));
            var envelopeType = typeof(ActionEnvelope<>).MakeGenericType(actionType);
            return JsonSerializer.Deserialize(json, envelopeType, Options);
        }
    }
}
