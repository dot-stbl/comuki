using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Comuki.Platform.Routing.Forwarding;

/// <inheritdoc />
public sealed partial class YarpUpstreamSender(
    TimeProvider timeProvider,
    IHttpForwarder forwarder,
    HttpMessageInvoker httpClient,
    IOptions<RotationOptions> options,
    IQuotaExhaustionDetector detector,
    ILogger<YarpUpstreamSender> logger) : IUpstreamSender
{
    private readonly string upstreamUrl = options.Value.UpstreamUrl;

    public async Task<UpstreamSendResult> SendOnceAsync(
        HttpContext context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var transformer = new RotatingTransformer(apiKey, timeProvider, detector);

        var error = await forwarder.SendAsync(context, upstreamUrl, httpClient, new ForwarderRequestConfig(), transformer, cancellationToken)
            .ConfigureAwait(false);

        if (error != ForwarderError.None)
        {
            var exception = context.GetForwarderErrorFeature()?.Exception;
            LogUpstreamForwardError(logger, error, exception?.Message);
        }

        return transformer.Result;
    }

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "upstream_forward_error: YARP forwarder returned {ForwarderError}; exception: {ExceptionMessage}")]
    private static partial void LogUpstreamForwardError(ILogger logger, ForwarderError forwarderError, string? exceptionMessage);
}
