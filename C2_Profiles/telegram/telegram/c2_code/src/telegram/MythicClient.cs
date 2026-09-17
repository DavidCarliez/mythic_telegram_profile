using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using PushC2Services;

namespace TelegramC2;

internal sealed class MythicClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly PushC2.PushC2Client _client;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic>? _stream;
    private CancellationToken _cancellationToken;
    private Task? _receiver;

    public MythicClient(string address)
    {
        _channel = GrpcChannel.ForAddress(address);
        _client = new PushC2.PushC2Client(_channel);
    }

    public event Func<PushC2MessageFromMythic, Task>? MessageReceived;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        await EnsureConnectedAsync(cancellationToken);
    }

    public Task SendAsync(string trackingId, string message)
    {
        return WriteAsync(new PushC2MessageFromAgent
        {
            C2ProfileName = "telegram",
            Base64Message = ByteString.CopyFromUtf8(message),
            TrackingID = trackingId,
            RemoteIP = string.Empty
        });
    }

    public Task DisconnectAsync(string trackingId)
    {
        return WriteAsync(new PushC2MessageFromAgent
        {
            C2ProfileName = "telegram",
            TrackingID = trackingId,
            AgentDisconnected = true
        });
    }

    private async Task WriteAsync(PushC2MessageFromAgent message)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await EnsureConnectedAsync(_cancellationToken);
            AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic> stream =
                _stream ?? throw new InvalidOperationException("Mythic stream is not connected.");

            await _writeLock.WaitAsync(_cancellationToken);
            try
            {
                await stream.RequestStream.WriteAsync(message);
                return;
            }
            catch when (attempt == 0 && !_cancellationToken.IsCancellationRequested)
            {
                await InvalidateAsync(stream);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        throw new InvalidOperationException("Failed to write to the Mythic stream.");
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_stream is not null)
            {
                return;
            }

            AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic> stream =
                _client.StartPushC2StreamingOneToMany(cancellationToken: cancellationToken);
            await stream.RequestStream.WriteAsync(new PushC2MessageFromAgent
            {
                C2ProfileName = "telegram"
            });
            _stream = stream;
            _receiver = ReceiveAsync(stream, cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task ReceiveAsync(
        AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (PushC2MessageFromMythic message in
                           stream.ResponseStream.ReadAllAsync(cancellationToken))
            {
                Func<PushC2MessageFromMythic, Task>? handler = MessageReceived;
                if (handler is null)
                {
                    continue;
                }

                try
                {
                    await handler(message);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Failed to deliver a Mythic response: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Mythic stream failed: {exception.Message}");
        }
        finally
        {
            await InvalidateAsync(stream);
        }
    }

    private async Task InvalidateAsync(
        AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic> stream)
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (ReferenceEquals(_stream, stream))
            {
                _stream = null;
                stream.Dispose();
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        AsyncDuplexStreamingCall<PushC2MessageFromAgent, PushC2MessageFromMythic>? stream;
        await _connectionLock.WaitAsync();
        try
        {
            stream = _stream;
            _stream = null;
        }
        finally
        {
            _connectionLock.Release();
        }

        if (stream is not null)
        {
            await _writeLock.WaitAsync();
            try
            {
                await stream.RequestStream.CompleteAsync();
            }
            catch
            {
            }
            finally
            {
                _writeLock.Release();
                stream.Dispose();
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

        _writeLock.Dispose();
        _connectionLock.Dispose();
        _channel.Dispose();
    }
}
