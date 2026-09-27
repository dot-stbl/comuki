using Comuki.Host.Errors;
using Comuki.Host.Errors.Core;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Errors;

/// <summary>
/// The one core <c>ValidationException</c> → 400 shape (design D6),
/// byte-pinned against the grouping the five per-module runners shipped:
/// one key per field (ordinal grouping on the property name), each key
/// holding that field's failure messages in failure order. The FE forms
/// gate their per-field errors on this dictionary — the migration of the
/// remaining runners depends on it not drifting.
/// </summary>
public sealed class ValidationProblemHandlerShould
{
    [Fact(DisplayName = "Given two failures on one field and one on another, when the handler answers, then errors group by field in failure order")]
    public void GroupsFailuresByFieldInFailureOrder()
    {
        var handler = new ValidationProblemHandler();

        var answer = handler.Answer(new ValidationException(
        [
            new ValidationFailure("Name", "name must not be empty"),
            new ValidationFailure("Name", "name must be at most 64 characters"),
            new ValidationFailure("Slug", "slug must be lowercase"),
        ]));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.ValidationErrors.ShouldNotBeNull();
        answer.ValidationErrors.Count.ShouldBe(2);
        answer.ValidationErrors["Name"].ShouldBe(["name must not be empty", "name must be at most 64 characters"]);
        answer.ValidationErrors["Slug"].ShouldBe(["slug must be lowercase"]);
    }

    [Fact(DisplayName = "Given a ValidationException, when resolved through the registry, then the core validation row answers")]
    public void ValidationExceptionResolvesThroughRegistry()
    {
        var registry = new ProblemHandlerRegistry([], NullLogger<ProblemHandlerRegistry>.Instance);

        var answer = registry.Resolve(new ValidationException([new ValidationFailure("Slug", "slug must be lowercase")]));

        answer.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        answer.ValidationErrors.ShouldNotBeNull();
        answer.ValidationErrors.Keys.ShouldBe(["Slug"]);
    }
}
