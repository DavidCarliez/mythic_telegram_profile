using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using PushC2Services;

namespace TelegramC2;

internal sealed class MythicClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly PushC2.PushC2Client _client;
    private AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic>? _stream;
    private Task? _receiver;

    public MythicClient(string address)
    {
        _channel = GrpcChannel.ForAddress(address);
        _client = new PushC2.PushC2Client(_channel);
    }

    public event Func<PushC2MessageFromMythic, Task>? MessageReceived;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stream = _client.StartPushC2StreamingOneToMany(cancellationToken: cancellationToken);
        await _stream.RequestStream.WriteAsync(new PushC2MessageFromAgent
        {
            C2ProfileName = "telegram"
        });
        _receiver = ReceiveAsync(cancellationToken);
    }

    public async Task SendAsync(string trackingId, string message)
    {
        AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic> stream =
            _stream ?? throw new InvalidOperationException("Mythic stream is not connected.");
        await stream.RequestStream.WriteAsync(new PushC2MessageFromAgent
        {
            C2ProfileName = "telegram",
            Base64Message = ByteString.CopyFromUtf8(message),
            TrackingID = trackingId,
            RemoteIP = string.Empty
        });
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            await foreach (PushC2MessageFromMythic message in
                           _stream.ResponseStream.ReadAllAsync(cancellationToken))
            {
                Func<PushC2MessageFromMythic, Task>? handler = MessageReceived;
                if (handler is not null)
                {
                    await handler(message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            try
            {
                await _stream.RequestStream.CompleteAsync();
            }
            catch
            {
            }
        }

        if (_receiver is not null)
        {
            try
            {
                await _receiver;
            }
            catch
            {
            }
        }

        _stream?.Dispose();
        _channel.Dispose();
    }
}
