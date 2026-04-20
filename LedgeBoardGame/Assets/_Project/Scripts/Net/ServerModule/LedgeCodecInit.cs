using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Magi.LedgeBoardGame.Models;
using MagiGameServer.Codec;

namespace Magi.LedgeBoardGame.ServerModule
{
    /// Per-game JsonConverter registration for the MagiGameServer.Codec wire
    /// pipeline. Kept separate from LedgeGameModule so the Unity-only client
    /// path (LedgeBoardSessionDriver) can trigger registration without
    /// instantiating a full game module, and so adding more converters later
    /// is a single-file diff.
    ///
    /// Why this exists at all: System.Text.Json 8 refuses to bind readonly
    /// struct properties through a parameterised constructor when the type
    /// also has the implicit parameterless struct ctor. SpaceId fits that
    /// shape exactly — round-tripping (1,5) through
    /// JsonSerializer.Deserialize yields SpaceId(0,0). Rather than adding
    /// STJ as a reference to the game-domain asmdef (which would pull the
    /// polyfill-collision risk flagged in feedback memory), the fix lives
    /// here: an explicit JsonConverter handles the (de)serialisation and
    /// the codec's AddExternalConverter seam plumbs it into both the
    /// shared Options instance and any ASP.NET JsonOptions the host
    /// configures.
    public static class LedgeCodecInit
    {
        private static int _registered;

        /// Idempotent; safe to call from every hot path that might race
        /// the first Serialize/Deserialize. Interlocked ensures the
        /// underlying AddExternalConverter runs at most once even if two
        /// LedgeGameModule instances are created concurrently.
        public static void EnsureRegistered()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _registered, 1, 0) != 0) return;
            EnvelopeCodec.AddExternalConverter(new SpaceIdJsonConverter());
        }
    }

    internal sealed class SpaceIdJsonConverter : JsonConverter<SpaceId>
    {
        public override SpaceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return default;
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for SpaceId, got {reader.TokenType}");

            int boardId = 0, id = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return new SpaceId(boardId, id);
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

                string name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "boardId", StringComparison.OrdinalIgnoreCase))
                    boardId = reader.GetInt32();
                else if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                    id = reader.GetInt32();
                else
                    reader.Skip();
            }
            throw new JsonException("Unterminated SpaceId object");
        }

        public override void Write(Utf8JsonWriter writer, SpaceId value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("boardId", value.BoardId);
            writer.WriteNumber("id", value.Id);
            writer.WriteEndObject();
        }
    }
}
