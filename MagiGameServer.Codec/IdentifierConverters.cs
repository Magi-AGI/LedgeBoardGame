using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Codec
{
    // System.Text.Json's default handling for a struct-with-Value property
    // emits { "value": ... } — a nested object where a bare scalar belongs.
    // These converters unwrap the strong-typed identifiers back to the
    // scalar their Value field holds, so the wire looks like
    // "session":"s" rather than "session":{"value":"s"}. Worth the code
    // because the alternative (stripping the type safety off identifiers
    // to get nice JSON) would cost us the compile-time seat-vs-session
    // mix-up protection the identifiers exist for.

    internal sealed class SessionIdConverter : JsonConverter<SessionId>
    {
        public override SessionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return default;
            return new SessionId(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    internal sealed class SeatIdConverter : JsonConverter<SeatId>
    {
        public override SeatId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new SeatId(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, SeatId value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Value);
    }

    internal sealed class ClientSeqConverter : JsonConverter<ClientSeq>
    {
        public override ClientSeq Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new ClientSeq(reader.GetInt64());

        public override void Write(Utf8JsonWriter writer, ClientSeq value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Value);
    }

    internal sealed class ServerSeqConverter : JsonConverter<ServerSeq>
    {
        public override ServerSeq Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new ServerSeq(reader.GetInt64());

        public override void Write(Utf8JsonWriter writer, ServerSeq value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Value);
    }
}
