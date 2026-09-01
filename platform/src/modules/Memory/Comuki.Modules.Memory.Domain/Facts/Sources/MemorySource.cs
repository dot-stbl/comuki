namespace Comuki.Modules.Memory.Domain.Facts.Sources;

/// <summary>How a fact entered memory.</summary>
public enum MemorySource
{
    /// <summary>Written by the chat graph (memory.write tool or a «запомни» command).</summary>
    Chat = 1,

    /// <summary>Written by a human through an explicit command.</summary>
    Human = 2,

    /// <summary>Written from a run (verify outcome, worker report).</summary>
    Run = 3,

    /// <summary>Approved out of a learning candidate.</summary>
    LearningApproved = 4,
}
