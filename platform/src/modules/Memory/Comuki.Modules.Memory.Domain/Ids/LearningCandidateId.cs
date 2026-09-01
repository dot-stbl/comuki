namespace Comuki.Modules.Memory.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a learning candidate (UUIDv7, Postgres
/// <c>uuid</c>).
/// </summary>
/// <param name="Value"></param>
public readonly record struct LearningCandidateId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static LearningCandidateId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
