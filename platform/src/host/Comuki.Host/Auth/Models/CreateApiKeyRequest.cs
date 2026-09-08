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
    /// <summary>Rules: user id, label length, expiry in the future when present.</summary>
    public CreateApiKeyRequestValidator()
    {
        RuleFor(static request => request.UserId)
            .NotEqual(Guid.Empty);

        RuleFor(static request => request.Label)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(static request => request.ExpiresAt)
            .GreaterThan(static _ => DateTimeOffset.UtcNow)
            .When(static request => request.ExpiresAt.HasValue);

        RuleFor(static request => request.TenantProjectId)
            .NotEqual(Guid.Empty)
            .When(static request => request.TenantProjectId.HasValue);
    }
}
