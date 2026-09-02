using Comuki.Host.Translator.Execution.Loop;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator;

/// <summary>
/// The worker's outer loop: claim → execute → report → repeat until the
/// process is stopped. Failures propagate and stop the host — an ephemeral
/// worker container is meant to die and be replaced by the supervisor, not
/// to limp along in an unknown state. Between empty claims it waits the
/// configured poll interval.
/// </summary>
/// <param name="loop"></param>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class TranslatorHostedService(
    TranslatorLoop loop,
    IOptions<TranslatorOptions> options,
    ILogger<TranslatorHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Translator worker starting (profile {ProfileKey}, ref {ProfilesRef})", options.Value.ProfileKey, options.Value.ProfilesRef);

        while (!stoppingToken.IsCancellationRequested)
        {
            var ran = await loop.TryRunOnceAsync(stoppingToken);
            if (!ran)
            {
                await Task.Delay(options.Value.ClaimPollInterval, stoppingToken);
            }
        }
    }
}
