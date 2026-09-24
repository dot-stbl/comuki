using Comuki.Modules.Verify.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Verify.Domain.Exceptions;

/// <summary>
/// Generic-command run lookup miss — controller maps this to 404.
/// </summary>
public sealed class GenericCommandRunNotFoundException(GenericCommandRunId runId) : DomainException(
    "verify.generic_command_run_not_found",
    $"generic command run {runId} not found")
{
    /// <summary>The id that was looked up.</summary>
    public GenericCommandRunId RunId { get; } = runId;
}
