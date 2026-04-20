using System;
using System.Collections.Generic;
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
    ///   Deserializes through ActionEnvelope&lt;JsonElement&gt; as an
    ///   intermediate, then resolves the Action element against the
    ///   module-supplied actionType and boxes into
    ///   ActionEnvelope&lt;object&gt; — the exact non-generic shape
    ///   Session.Apply consumes, with no reflection glue at the seam.
    ///
    /// Options are immutable and shared: enum-as-string,
    /// strong-typed-identifier converters, camelCase property naming.
    /// Shared instance so callers don't pay the ~100ms per-options warm-up
    /// on every call, and so a mismatch between server and client casing
    /// is impossible (there's one set of options, not two).
    public static class EnvelopeCodec
    {
        // Declared before Options so the Options field initializer — which
        // calls ConfigureOptions → locks _converterLock — observes a
        // fully-constructed list and lock. C# runs static field initializers
        // in source order; inverting this would NRE on the very first call.
        private static readonly List<JsonConverter> _externalConverters = new List<JsonConverter>();
        private static readonly object _converterLock = new object();

        public static JsonSerializerOptions Options { get; } = BuildOptions();

        private static JsonSerializerOptions BuildOptions()
        {
            var o = new JsonSerializerOptions();
            ConfigureOptions(o);
            return o;
        }

        /// Applies the wire-codec policy (camelCase, enum-as-string,
        /// strong-typed-identifier converters) to an existing
        /// JsonSerializerOptions. Used by the ASP.NET host to ensure
        /// `Results.Ok` / `ReadFromJsonAsync` on the HTTP pipeline
        /// produce the exact same wire shape as direct codec calls — if
        /// the host used default options, HTTP bodies and WebSocket
        /// frames would disagree on whether SessionId is "s" or
        /// {"value":"s"}.
        public static void ConfigureOptions(JsonSerializerOptions target)
        {
            target.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            target.PropertyNameCaseInsensitive = false;
            target.WriteIndented = false;
            target.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            target.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            target.Converters.Add(new SessionIdConverter());
            target.Converters.Add(new SeatIdConverter());
            target.Converters.Add(new ClientSeqConverter());
            target.Converters.Add(new ServerSeqConverter());
            lock (_converterLock)
            {
                foreach (var c in _externalConverters) target.Converters.Add(c);
            }
        }

        /// Game-specific JsonConverter registration seam. Called once per
        /// converter type from the owning game module's constructor, which
        /// runs at app startup (server: `new LedgeGameModule()` inside
        /// registerModules; client: LedgeBoardSessionDriver ctor). Both
        /// paths fire before the first Serialize/Deserialize on the static
        /// Options instance, so adding to Options.Converters is safe —
        /// STJ freezes options only on first use. Idempotent on converter
        /// type: calling twice with a SpaceIdJsonConverter produces one
        /// registration. Late calls (after first use) log to stderr and
        /// drop silently rather than throw, so a mid-session bootstrap
        /// failure degrades to "types serialize as defaults" rather than
        /// "server crashes".
        public static void AddExternalConverter(JsonConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            lock (_converterLock)
            {
                var t = converter.GetType();
                foreach (var c in _externalConverters)
                    if (c.GetType() == t) return;
                _externalConverters.Add(converter);
                try { Options.Converters.Add(converter); }
                catch (InvalidOperationException)
                {
                    Console.Error.WriteLine(
                        $"[EnvelopeCodec] {t.Name} registered after options froze; wire shape for its type will fall back to STJ defaults.");
                }
            }
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
