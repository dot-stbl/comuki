namespace Comuki.Host.Brain.Brain.Exceptions;

/// <summary>
/// No usable brain model configuration — neither boot-time values, env
/// vars nor secret refs produced a complete Endpoint/ApiKey/ModelId.
/// Surfaces as a gRPC Internal fault whose detail carries the operator
/// setup hint (see <c>BrainOptions</c> env variable names).
/// </summary>
public sealed class BrainModelNotConfiguredException(string message) : InvalidOperationException(message);
