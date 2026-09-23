using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Verify.Domain.Exceptions;

/// <summary>
/// The requested status transition is illegal — terminal runs never move.
/// </summary>
public sealed class IllegalGenericCommandStatusTransitionException(
    GenericCommandStatus from,
    GenericCommandStatus to) : DomainException(
    "verify.illegal_status_transition",
    $"generic command run cannot transition from {from} to {to}")
{
    /// <summary>Current status.</summary>
    public GenericCommandStatus From { get; } = from;

    /// <summary>Requested status.</summary>
    public GenericCommandStatus To { get; } = to;
}
