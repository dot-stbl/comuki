using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Procedures.Application.Runtime;

/// <summary>
/// Typed refusal from the procedure runtime: repair boundary exhaustion,
/// budget exhaustion, or an inconclusive outcome that must not loop.
/// </summary>
public sealed class ProcedureRuntimeException(string code, string message)
    : DomainException(code, message)
{
    /// <summary>Repair boundary hit its generation cap — escalate to a human.</summary>
    public const string GenerationsExhausted = "procedures.repair.generations_exhausted";

    /// <summary>Budget slice consumed before the generation cap — escalate.</summary>
    public const string BudgetExhausted = "procedures.repair.budget_exhausted";

    /// <summary>An inconclusive port tried to open a semantic generation — it must escalate instead.</summary>
    public const string InconclusiveDoesNotLoop = "procedures.repair.inconclusive_does_not_loop";

    /// <summary>A procedure key arrived empty or whitespace — caller-side bug, refuse loudly.</summary>
    public const string ProcedureKeyEmpty = "procedures.runtime.procedure_key_empty";
}
