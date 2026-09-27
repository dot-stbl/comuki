using Comuki.Shared.Kernel.Exceptions;
using FluentValidation;

namespace Comuki.Host.Errors;

/// <summary>
/// The typed registry the composition-root handler resolves through:
/// exact exception type first, then the base-type chain, so the most-derived
/// registration wins without ordering tricks. The kernel arms (provider
/// family, budget, <see cref="DomainException"/> 422 default,
/// <see cref="ValidationException"/> 400, unhandled 500) are built-in rows —
/// every thrown exception is answered, none falls through unmapped.
/// </summary>
/// <param name="moduleHandlers">DI-collected per-error-type handlers; each exact type must be sealed (asserted at boot).</param>
/// <param name="logger">Warns when a DomainException subclass reaches the 422 default without a dedicated row — fail-open on the wire, visible in ops.</param>
public sealed class ProblemHandlerRegistry(
    IEnumerable<IProblemHandler> moduleHandlers,
    ILogger<ProblemHandlerRegistry> logger)
{
    private readonly IReadOnlyDictionary<Type, IProblemHandler> handlers = ProblemHandlerRegistryConstruction.Build(moduleHandlers);

    /// <summary>
    /// Resolves the wire row for a thrown exception: the exact runtime type
    /// is consulted first, then each base type up to <see cref="Exception"/>.
    /// </summary>
    /// <param name="exception">The thrown exception.</param>
    /// <returns>The row the composition root executes.</returns>
    public ProblemAnswer Resolve(Exception exception)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (handlers.TryGetValue(type, out var handler))
            {
                var answer = handler.Answer(exception);

                if (handler.ExceptionType == typeof(DomainException) && exception.GetType() != typeof(DomainException))
                {
                    logger.LogWarning(
                        "Domain exception {ExceptionType} has no dedicated problem handler; answering 422 with code {ProblemCode}",
                        exception.GetType().Name,
                        answer.Code);
                }

                return answer;
            }
        }

        throw new InvalidOperationException(
            $"No problem handler resolved for {exception.GetType().Name}; the built-in Exception fallback row is missing.");
    }
}

/// <summary>
/// Startup construction: seeds the built-in kernel rows, then validates and
/// adds the module handlers. Loud failures at boot — a non-sealed exact type
/// or a duplicate registration is a wiring bug, not a runtime surprise.
/// </summary>
file static class ProblemHandlerRegistryConstruction
{
    public static IReadOnlyDictionary<Type, IProblemHandler> Build(IEnumerable<IProblemHandler> moduleHandlers)
    {
        Dictionary<Type, IProblemHandler> built = new()
        {
            [typeof(ProviderTimeoutException)] = new Core.ProviderTimeoutProblemHandler(),
            [typeof(ProviderNotFoundException)] = new Core.ProviderNotFoundProblemHandler(),
            [typeof(ProviderForbiddenException)] = new Core.ProviderForbiddenProblemHandler(),
            [typeof(BudgetExceededException)] = new Core.BudgetExceededProblemHandler(),
            [typeof(ProviderException)] = new Core.ProviderExceptionProblemHandler(),
            [typeof(DomainException)] = new Core.DomainExceptionProblemHandler(),
            [typeof(ValidationException)] = new Core.ValidationProblemHandler(),
            [typeof(Exception)] = new Core.FallbackProblemHandler(),
        };

        foreach (var handler in moduleHandlers)
        {
            if (!handler.ExceptionType.IsSealed)
            {
                throw new InvalidOperationException(
                    $"Problem handler {handler.GetType().Name} registers non-sealed exception type {handler.ExceptionType.Name}; seal the exception so a subclass cannot silently take its row.");
            }

            if (built.TryGetValue(handler.ExceptionType, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate problem handler for {handler.ExceptionType.Name}: {existing.GetType().Name} and {handler.GetType().Name}.");
            }

            built[handler.ExceptionType] = handler;
        }

        return built;
    }
}
