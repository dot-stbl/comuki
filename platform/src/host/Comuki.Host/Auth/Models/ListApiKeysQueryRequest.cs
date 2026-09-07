using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>
/// Query record for <c>GET /api/v1/keys</c> (issue #45 / F13). Bound from
/// the URL query string by the controller — <c>userId</c> is the
/// optional owner-user-id filter; <c>page</c> is 1-based; <c>pageSize</c>
/// lives in <c>1..100</c>.
/// </summary>
public sealed class ListApiKeysQueryRequest
{
    /// <summary>Optional owner user id filter.</summary>
    public Guid? UserId { get; init; }

    /// <summary>1-based page index; default 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size (1..100); default 100.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>
/// Structural validation for <see cref="ListApiKeysQueryRequest"/>.
/// Pagination bounds + non-empty user id when present.
/// </summary>
public sealed class ListApiKeysQueryRequestValidator : AbstractValidator<ListApiKeysQueryRequest>
{
    /// <summary>Rules: pagination bounds + non-empty user-id when present.</summary>
    public ListApiKeysQueryRequestValidator()
    {
        RuleFor(static request => request.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(static request => request.PageSize)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(100);

        RuleFor(static request => request.UserId!.Value)
            .NotEqual(Guid.Empty)
            .When(static request => request.UserId.HasValue);
    }
}
