namespace Comuki.Modules.Intake.Application.Ports.Sync;

/// <summary>
/// The webhook pipeline's answer to the hook endpoint: an HTTP status, a
/// delivery outcome label (see <c>DeliveryOutcomes</c>) and — on errors —
/// a stable problem code.
/// </summary>
/// <param name="StatusCode"></param>
/// <param name="Outcome"></param>
/// <param name="Code">Stable problem code for 4xx answers; null on 200.</param>
/// <param name="Detail"></param>
public sealed record WebhookReceipt(
    int StatusCode,
    string Outcome,
    string? Code = null,
    string? Detail = null)
{
    /// <summary>A processed (or deliberately ignored) delivery — 200 OK.</summary>
    /// <param name="outcome"></param>
    /// <param name="detail"></param>
    /// <returns></returns>
    public static WebhookReceipt Ok(string outcome, string? detail = null)
    {
        return new WebhookReceipt(200, outcome, null, detail);
    }

    /// <summary>Unknown provider or connection — 404.</summary>
    /// <param name="code"></param>
    /// <param name="detail"></param>
    /// <returns></returns>
    public static WebhookReceipt NotFound(string code, string detail)
    {
        return new WebhookReceipt(404, "not_found", code, detail);
    }

    /// <summary>Signature verification failed — 401 (the signature IS the auth).</summary>
    /// <param name="detail"></param>
    /// <returns></returns>
    public static WebhookReceipt SignatureInvalid(string detail)
    {
        return new WebhookReceipt(401, "rejected", "intake.signature_invalid", detail);
    }
}
