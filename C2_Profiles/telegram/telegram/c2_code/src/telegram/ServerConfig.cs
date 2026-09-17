using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelegramC2;

internal sealed class ServerConfig
{
    [JsonPropertyName("botToken")]
    public string BotToken { get; set; } = string.Empty;

    [JsonPropertyName("apiBase")]
    public string ApiBase { get; set; } = "https://api.telegram.org";

    [JsonPropertyName("pollTimeout")]
    public int PollTimeout { get; set; } = 20;

    [JsonPropertyName("mythicGrpc")]
    public string MythicGrpc { get; set; } = "http://127.0.0.1:17444";

    public static ServerConfig Load(string path)
    {
        string json = File.ReadAllText(path);
        ServerConfig config = JsonSerializer.Deserialize<ServerConfig>(json)
            ?? throw new InvalidOperationException("Telegram server configuration is empty.");
        config.Validate();
        return config;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
        {
            throw new InvalidOperationException("botToken is required in config.json.");
        }

        if (!Uri.TryCreate(ApiBase, UriKind.Absolute, out Uri? apiUri) ||
            (apiUri.Scheme != Uri.UriSchemeHttps && apiUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("apiBase must be an absolute HTTP or HTTPS URL.");
        }

        if (!Uri.TryCreate(MythicGrpc, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("mythicGrpc must be an absolute URL.");
        }

        if (PollTimeout is < 1 or > 50)
        {
            throw new InvalidOperationException("pollTimeout must be between 1 and 50 seconds.");
        }
    }
}
