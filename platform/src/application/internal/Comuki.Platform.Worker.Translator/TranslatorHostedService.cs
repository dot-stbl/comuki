using Comuki.Platform.Worker.Translator.Interfaces;

namespace Comuki.Platform.Worker.Translator;

/// <summary>
/// Hosted entry point for the worker. For 04-01 it just logs the wiring —
/// real task dispatch (claim from Postgres, then <see cref="ITranslator.TranslateAsync"/>)
/// lands in 04-03. The point of this stub is to prove the DI graph (PiRunner →
/// Translator → hosted service) boots and the process stays alive long enough
/// for the podman-compose / docker-compose healthcheck to pass.
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
