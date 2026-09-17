using TelegramC2;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

ServerConfig config = ServerConfig.Load("config.json");
using var telegram = new TelegramApiClient(config.BotToken, config.ApiBase);
await using var mythic = new MythicClient(config.MythicGrpc);
using var server = new TelegramServer(config, telegram, mythic);
await server.RunAsync(cancellation.Token);
