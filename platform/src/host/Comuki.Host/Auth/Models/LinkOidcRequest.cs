using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>OIDC link body (POST /api/v1/users/{userId}/oidc-link).</summary>
public sealed class LinkOidcRequest
{
    /// <summary>Provider key (matches an entry in <c>auth:oidc:providers</c>).</summary>
    public required string Provider { get; init; } = string.Empty;

    /// <summary>The external <c>sub</c> claim.</summary>
    public required string SubjectId { get; init; } = string.Empty;
}

/// <summary>Validation of <see cref="LinkOidcRequest"/>.</summary>
public sealed class LinkOidcRequestValidator : AbstractValidator<LinkOidcRequest>
{
    /// <summary>Rules: provider and subject id must be non-empty with sane upper bounds.</summary>
    public LinkOidcRequestValidator()
    {
        RuleFor(static request => request.Provider)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(static request => request.SubjectId)
            .NotEmpty()
            .MaximumLength(256);
    }
}
