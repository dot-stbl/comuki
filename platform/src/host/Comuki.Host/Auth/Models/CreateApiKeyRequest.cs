using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>Create API key body (POST /api/v1/keys).</summary>
public sealed class CreateApiKeyRequest
{
    /// <summary>Owner user id.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Human-readable label.</summary>
    public required string Label { get; init; } = string.Empty;

    /// <summary>Optional expiry timestamp (UTC).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Optional tenant scope. When set, the key only authenticates
    /// requests that carry the matching <c>X-Comuki-Tenant</c> header.
    /// The host validates that the requesting subject has the right to
    /// scope a key to this project — admin-only today.
    /// </summary>
    public Guid? TenantProjectId { get; init; }
}

/// <summary>Validation of <see cref="CreateApiKeyRequest"/>.</summary>
public sealed class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    private readonly TimeProvider clock;

    /// <summary>Rules: user id, label length, expiry in the future when present.</summary>
    /// <param name="clock">
    ///     Time source for "now" comparisons. Injected so the "expiry must be in the future"
    ///     rule resolves per request — a captured <c>DateTimeOffset.UtcNow</c> at validator
    ///     construction would drift forward by the validator's lifetime and start rejecting
    ///     freshly issued near-future expiries within hours.
    /// </param>
    public CreateApiKeyRequestValidator(TimeProvider clock)
    {
        this.clock = clock;

        RuleFor(static request => request.UserId)
            .NotEqual(Guid.Empty);

        RuleFor(static request => request.Label)
            .NotEmpty()
            .MaximumLength(128);

        // Per-request "now" — a captured UtcNow at validator-construction time would
        // drift forward by however long the process lives and start rejecting
        // freshly issued near-future expiries within hours.
        RuleFor(static request => request.ExpiresAt)
            .GreaterThan(_ => this.clock.GetUtcNow())
            .When(static request => request.ExpiresAt.HasValue);

        RuleFor(static request => request.TenantProjectId)
            .NotEqual(Guid.Empty)
            .When(static request => request.TenantProjectId.HasValue);
    }
}
