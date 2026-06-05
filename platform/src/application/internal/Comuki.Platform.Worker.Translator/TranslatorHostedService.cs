namespace Comuki.Platform.Worker.Translator;

/// <summary>
/// Placeholder BackgroundService. The real work — Process.Start(pi) +
/// stream-json parsing + gRPC bidi stream to Orchestrator — lands in plan 04-03
/// (gRPC impl) and 04-04 (handshake). This stub exists so the worker project
/// compiles and the host can boot, which is what 04-01 needs to prove the
/// project structure is wired.
/// </summary>
public sealed class TranslatorHostedService(ILogger<TranslatorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker.Translator hosted service starting (plan 04-01 stub; real work in 04-03/04)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        logger.LogInformation("Worker.Translator hosted service stopping");
    }
}
