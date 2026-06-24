using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Comuki.Platform.Routing.Forwarding;

/// <inheritdoc />
public sealed class YarpUpstreamSender(
    IHttpForwarder forwarder,
    IQuotaExhaustionDetector detector,
    HttpMessageInvoker httpClient,
    IOptions<RotationOptions> options) : IUpstreamSender
{
    private readonly string upstreamUrl = options.Value.UpstreamUrl;

    public async Task<UpstreamSendResult> SendOnceAsync(
        HttpContext context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var transformer = new RotatingTransformer(apiKey, detector);

        await forwarder.SendAsync(context, this.upstreamUrl, httpClient, new ForwarderRequestConfig(), transformer, cancellationToken)
            .ConfigureAwait(false);

        return transformer.Result;
    }
}
