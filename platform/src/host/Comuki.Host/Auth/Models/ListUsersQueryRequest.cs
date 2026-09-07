using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>
/// Query record for <c>GET /api/v1/users</c> (issue #45 / F13). Bound from
/// the URL query string by the controller — <c>emailContains</c> is the
/// optional case-insensitive substring filter on email; <c>page</c> is
/// 1-based; <c>pageSize</c> lives in <c>1..100</c>.
/// </summary>
public sealed class ListUsersQueryRequest
{
    /// <summary>Optional case-insensitive substring filter on email.</summary>
    public string? EmailContains { get; init; }

    /// <summary>1-based page index; default 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size (1..100); default 100.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>
/// Structural validation for <see cref="ListUsersQueryRequest"/>. Business
/// rules (which fields are filterable) live in the handler; the request
/// only enforces the wire-format invariants.
/// </summary>
public sealed class ListUsersQueryRequestValidator : AbstractValidator<ListUsersQueryRequest>
{
    /// <summary>Rules: pagination bounds + email-substring length.</summary>
    public ListUsersQueryRequestValidator()
    {
        RuleFor(static request => request.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(static request => request.PageSize)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(100);

        RuleFor(static request => request.EmailContains)
            .MaximumLength(320)
            .When(static request => request.EmailContains is not null);
    }
}
