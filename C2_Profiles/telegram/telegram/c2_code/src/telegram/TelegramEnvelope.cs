using System.Text.Json.Serialization;

namespace TelegramC2;

internal sealed class TelegramEnvelope
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sender_id")]
    public string SenderId { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("to_server")]
    public bool ToServer { get; set; }

    [JsonPropertyName("packet_id")]
    public string PacketId { get; set; } = string.Empty;

    [JsonPropertyName("chunk")]
    public int Chunk { get; set; }

    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
