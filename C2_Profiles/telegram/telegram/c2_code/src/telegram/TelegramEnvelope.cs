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

    [JsonPropertyName("reply_to")]
    public string ReplyToPacketId { get; set; } = string.Empty;

    [JsonPropertyName("sleep")]
    public int SleepSeconds { get; set; }

    [JsonPropertyName("jitter")]
    public int JitterPercent { get; set; }

    [JsonPropertyName("chunk")]
    public int Chunk { get; set; }

    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
