using Comuki.Platform.Routing.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Comuki.Platform.Routing.Forwarding;

/// <inheritdoc />
public sealed partial class KeyRotatingForwarder(
    IKeyPool keyPool,
    IUpstreamSender sender,
    ILogger<KeyRotatingForwarder> logger) : IKeyRotatingForwarder
{
    private const string OverloadedBody =
        """{"type":"error","error":{"type":"overloaded_error","message":"All Z.AI keys are in cooldown."}}""";

    public async Task ForwardAsync(HttpContext context, CancellationToken cancellationToken)
    {
        // Буферизуем тело запроса, чтобы повторить его на следующем ключе.
        context.Request.EnableBuffering();

        var maxAttempts = keyPool.Count;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            // YARP guards SendAsync with IsResponseSet = (StatusCode != 200 || HasStarted).
            // A previous exhausted attempt sets StatusCode to the upstream error code
            // without starting the response body, so we must reset to 200 before retry.
            if (attempt > 0 && !context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
            }

            var key = keyPool.TryAcquire();
            if (key is null)
            {
                break;
            }

            var result = await sender.SendOnceAsync(context, key, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case SendOutcome.Success:
                case SendOutcome.PassedThroughError:
                    return;

                case SendOutcome.Exhausted:
                    keyPool.MarkExhausted(key, result.RetryAfter);
                    LogKeyRotated(logger, result.RetryAfter);
                    continue;

                default:
                    return;
            }
        }

        await WriteAllKeysExhaustedAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAllKeysExhaustedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(OverloadedBody, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "key_rotated: Z.AI key exhausted, cooldown retry-after {RetryAfter}")]
    private static partial void LogKeyRotated(ILogger logger, TimeSpan? retryAfter);
}
