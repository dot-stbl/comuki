using Comuki.Platform.Routing.Interfaces;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Comuki.Platform.Routing.Forwarding;

/// <summary>
/// Per-request трансформер: подменяет ключ на исходящем запросе и инспектирует
/// ответ апстрима. Успех (2xx) стримит как есть; non-2xx читает (маленькое) тело
/// и через детектор решает — исчерпание (suppress, retry) или реальная ошибка
/// (пробросить клиенту). Результат — в <see cref="Result"/>.
/// </summary>
internal sealed class RotatingTransformer(string apiKey, IQuotaExhaustionDetector detector) : HttpTransformer
{
    public UpstreamSendResult Result { get; private set; } = UpstreamSendResult.PassedThroughError();

    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix,
        CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken)
            .ConfigureAwait(false);

        // Z.AI / Anthropic-совместимый эндпоинт принимает ключ в обоих заголовках.
        proxyRequest.Headers.Remove("Authorization");
        proxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        proxyRequest.Headers.Remove("x-api-key");
        proxyRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);
    }

    public override async ValueTask<bool> TransformResponseAsync(
        HttpContext httpContext,
        HttpResponseMessage? proxyResponse,
        CancellationToken cancellationToken)
    {
        if (proxyResponse is null)
        {
            // Сетевой/форвард сбой — апстрим не ответил. Forwarder уже выставит ошибку клиенту.
            this.Result = UpstreamSendResult.PassedThroughError();
            return false;
        }

        var status = (int)proxyResponse.StatusCode;
        if (status is >= 200 and < 300)
        {
            this.Result = UpstreamSendResult.Success();
            return await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken)
                .ConfigureAwait(false);
        }

        var body = await proxyResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detector.IsExhausted(status, body))
        {
            this.Result = UpstreamSendResult.Exhausted(ParseRetryAfter(proxyResponse));
            return false; // ничего не пишем клиенту — даём шанс retry
        }

        // Реальная не-quota ошибка — пробрасываем клиенту (тело уже прочитано).
        this.Result = UpstreamSendResult.PassedThroughError();
        httpContext.Response.StatusCode = status;
        if (proxyResponse.Content.Headers.ContentType is { } contentType)
        {
            httpContext.Response.ContentType = contentType.ToString();
        }

        await httpContext.Response.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is not { } date)
        {
            return null;
        }

        var remaining = date - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }
}
