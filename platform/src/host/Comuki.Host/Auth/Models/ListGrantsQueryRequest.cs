using FluentValidation;

namespace Comuki.Host.Auth.Models;

/// <summary>
/// Query record for <c>GET /api/v1/grants</c> (issue #45 / F13). Bound from
/// the URL query string by the controller — <c>subjectKind</c> is the
/// optional subject-kind filter (<c>"user"</c> / <c>"api-key"</c>); <c>subjectId</c>
/// narrows to a single subject row by id; <c>page</c> is 1-based;
/// <c>pageSize</c> lives in <c>1..100</c>.
/// </summary>
public sealed class ListGrantsQueryRequest
{
    /// <summary>Optional subject-kind filter (<c>"user"</c> or <c>"api-key"</c>).</summary>
    public string? SubjectKind { get; init; }

    /// <summary>Optional subject id filter.</summary>
    public Guid? SubjectId { get; init; }

    /// <summary>1-based page index; default 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size (1..100); default 100.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>
/// Structural validation for <see cref="ListGrantsQueryRequest"/>. The
/// kind vocabulary lives in <c>SubjectTypeKeys</c>; the request only
/// restricts pagination bounds and the kind length.
/// </summary>
public sealed class ListGrantsQueryRequestValidator : AbstractValidator<ListGrantsQueryRequest>
{
    /// <summary>Rules: pagination bounds + subject-kind vocabulary check + subject-id format.</summary>
    public ListGrantsQueryRequestValidator()
    {
        RuleFor(static request => request.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(static request => request.PageSize)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(100);

        RuleFor(static request => request.SubjectKind)
            .Must(static kind => kind is "user" or "api-key")
            .WithMessage("subjectKind must be either 'user' or 'api-key'")
            .When(static request => request.SubjectKind is not null);
    }
}
