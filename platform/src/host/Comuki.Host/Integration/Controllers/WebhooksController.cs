using Comuki.Modules.Integrations.Application.Ports.Sync;
using Comuki.Modules.Integrations.Application.Tickets;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Integration.Controllers;

/// <summary>
/// The anonymous webhook surface: <c>POST /api/hooks/{provider}/{key}</c>.
/// The per-connection key routes the letter to its connection BEFORE any
/// signature check (the signature secret lives on the connection); the
/// verified signature IS the auth. Every deliberately dropped letter —
/// replays, skips, filtered tickets, duplicates — answers 200 with an
/// outcome so trackers never retry what we chose to ignore.
/// </summary>
/// <param name="webhookIngestion">Service handling the integrations-from-webhook flow.</param>
/// <param name="logger"></param>
[ApiController]
[Route(ApiRoutes.HooksRoot)]
public sealed class WebhooksController(
    WebhookIngestionService webhookIngestion,
    ILogger<WebhooksController> logger) : ControllerBase
{
    /// <summary>Accepts one tracker webhook delivery.</summary>
    /// <param name="provider">Kebab-case source key (github | gitlab | yandex-tracker | jira).</param>
    /// <param name="key">Per-connection webhook routing key.</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{provider}/{key}")]
    [ProducesResponseType(typeof(WebhookAcceptedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReceiveAsync(string provider, string key, CancellationToken cancellationToken = default)
    {
        var delivery = await WebhookDeliveryReader.ReadAsync(Request, cancellationToken);
        var receipt = await webhookIngestion.HandleAsync(provider, key, delivery, cancellationToken);

        if (receipt.StatusCode is not 200)
        {
            logger.LogInformation(
                "Webhook from {Provider} answered {StatusCode} ({Code})",
                provider,
                receipt.StatusCode,
                receipt.Code ?? receipt.Outcome);
            return WebhookProblemRows.Problem(
                receipt.StatusCode,
                receipt.Code ?? "integration.webhook_rejected",
                receipt.StatusCode == StatusCodes.Status401Unauthorized ? "Webhook rejected" : "Not found",
                receipt.Detail ?? "the delivery was refused");
        }

        return Ok(new WebhookAcceptedResponse(receipt.Outcome, receipt.Detail));
    }
}

/// <summary>The 200 body of a processed (or deliberately ignored) delivery.</summary>
/// <param name="Outcome">Delivery outcome label (admitted | pending | filtered | skipped | duplicate | replay).</param>
/// <param name="Detail"></param>
public sealed record WebhookAcceptedResponse(string Outcome, string? Detail);

/// <summary>
/// Problem rows of the webhook value flow — the receipt is a result object,
/// not a thrown exception, so its 4xx rows stay endpoint-local (per the
/// domain-error-contract scope: only exception→ProblemDetails mapping
/// consolidates). Same shape the retired <c>IntegrationProblems.Problem</c> built.
/// </summary>
file static class WebhookProblemRows
{
    public static ActionResult Problem(int statusCode, string code, string title, string detail)
    {
        // Build with TypedResults.Problem so the title/type defaults and
        // extension shape stay canonical (issue #20), then wrap in
        // ObjectResult for the controller-side ActionResult contract.
        var typed = TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });

        return new ObjectResult(typed.ProblemDetails)
        {
            StatusCode = typed.StatusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}

/// <summary>Request → <see cref="WebhookDelivery"/> (raw body, headers, query).</summary>
file static class WebhookDeliveryReader
{
    public static async Task<WebhookDelivery> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in request.Headers)
        {
            if (values.Count > 0)
            {
                headers[name] = values.ToString();
            }
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in request.Query)
        {
            if (values.Count > 0)
            {
                query[name] = values.ToString();
            }
        }

        return new WebhookDelivery(memory.ToArray(), headers, query);
    }
}
