namespace Comuki.Platform.Routing.Forwarding;

/// <summary>Результат попытки форварда: исход + (для Exhausted) Retry-After.</summary>
public sealed record UpstreamSendResult(SendOutcome Outcome, TimeSpan? RetryAfter)
{
    public static UpstreamSendResult Success() => new(SendOutcome.Success, null);

    public static UpstreamSendResult Exhausted(TimeSpan? retryAfter) => new(SendOutcome.Exhausted, retryAfter);

    public static UpstreamSendResult PassedThroughError() => new(SendOutcome.PassedThroughError, null);
}
