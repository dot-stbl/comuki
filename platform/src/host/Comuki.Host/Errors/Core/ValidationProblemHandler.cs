using FluentValidation;

namespace Comuki.Host.Errors.Core;

/// <summary>
/// The one owner of the FluentValidation → 400 shape, replacing the five
/// copy-pasted groupings in the per-module runners: field name → that
/// field's failure messages, grouped by
/// <see cref="string" />
/// ordinal on the property name, messages in failure order. The composition
/// root emits a bare <c>TypedResults.ValidationProblem(errors)</c> from it —
/// the exact body the FE forms gate their per-field errors on.
/// </summary>
internal sealed class ValidationProblemHandler : IProblemHandler
{
    /// <inheritdoc />
    public Type ExceptionType => typeof(ValidationException);

    /// <inheritdoc />
    public ProblemAnswer Answer(Exception exception)
    {
        var validation = (ValidationException)exception;
        var errors = validation.Errors
            .GroupBy(static failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                static grouping => grouping.Key,
                static grouping => grouping.Select(static failure => failure.ErrorMessage).ToArray());

        return ProblemAnswer.Validation(errors);
    }
}
