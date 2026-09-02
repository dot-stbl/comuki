namespace Comuki.Host.Brain.Brain.Exceptions;

/// <summary>The agent loop hit its iteration cap without producing a result.</summary>
public sealed class BrainExhaustedException(int maxIterations) : Exception(
    $"the brain agent loop exceeded {maxIterations} model round-trips without a final result")
{
    /// <summary>The cap that was hit.</summary>
    public int MaxIterations { get; } = maxIterations;
}
