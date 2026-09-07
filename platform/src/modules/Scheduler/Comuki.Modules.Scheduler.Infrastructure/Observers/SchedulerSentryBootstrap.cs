using Comuki.Modules.Scheduler.Application.Options;
using Microsoft.Extensions.Configuration;

namespace Comuki.Modules.Scheduler.Infrastructure.Observers;

/// <summary>
/// One-shot initialiser for the Sentry SDK used by the scheduler
/// side-channel. The dispatcher needs <see cref="SentrySdk.CaptureEvent"/>
/// to work even though the SDK is otherwise invisible to the host —
/// calling <see cref="SentrySdk.Init"/> here keeps the rest of the host
/// from knowing Sentry exists.
/// <para>
/// When <see cref="SchedulerSentryOptions.Dsn"/> is null or whitespace
/// this method is a no-op: no <c>SentrySdk.Init</c> call, no transport
/// thread, no SDK footprint. The <see cref="SentrySchedulerObserver"/>
/// also gates on the same DSN so an unconfigured deployment never
/// reaches the capture path.
/// </para>
/// </summary>
public static class SchedulerSentryBootstrap
{
    /// <summary>
    /// Reads the bound <see cref="SchedulerSentryOptions"/> from
    /// configuration and, when a DSN is present, calls
    /// <see cref="SentrySdk.Init"/> with that DSN + environment.
    /// </summary>
    /// <param name="configuration">Application configuration root.</param>
    /// <returns><see langword="true"/> when the SDK was initialised, <see langword="false"/> otherwise.</returns>
    public static bool TryInitialize(IConfiguration configuration)
    {
        var sentryOptions = configuration
            .GetSection(SchedulerSentryOptions.SectionName)
            .Get<SchedulerSentryOptions>() ?? new SchedulerSentryOptions();

        if (string.IsNullOrWhiteSpace(sentryOptions.Dsn))
        {
            return false;
        }

        SentrySdk.Init(o =>
        {
            o.Dsn = sentryOptions.Dsn;
            if (!string.IsNullOrWhiteSpace(sentryOptions.Environment))
            {
                o.Environment = sentryOptions.Environment;
            }

            // Scheduler events are informational — drop the default
            // sample rate to 1.0 so the platform's own fires never
            // disappear from the Sentry stream. Operators tune via
            // Sentry's server-side sample rate per project.
            o.SampleRate = (float?)1.0;
        });

        return true;
    }
}
