using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Application.Compiler.Model;

/// <summary>
/// Typed refusal from the deterministic compile gate. Stable codes map to
/// distinct operator actions; the message always names the offending node.
/// </summary>
public sealed class ProcedureCompilerException(
    string code,
    string message) : DomainException(code, message)
{
    /// <summary>Cycle detected outside a repair boundary.</summary>
    public const string CycleDetected = "procedures.compiler.cycle_detected";

    /// <summary>An edge references a port the source kind does not declare.</summary>
    public const string PortNotFound = "procedures.compiler.port_not_found";

    /// <summary>Fan-out exceeds the platform ceiling at some depth.</summary>
    public const string FanOutExceeded = "procedures.compiler.fan_out_exceeded";
}
