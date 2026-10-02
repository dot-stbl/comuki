namespace Comuki.Modules.Procedures.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a <see cref="Patches.GraphPatch"/> — the durable
/// proposal object the brain drafts and Studio renders (design decision 5,
/// task 3.1). Generated with <c>Guid.CreateVersion7</c> so patch ids sort
/// by creation time; this is what the outbox carries in
/// <c>procedures.procedure.published.v1</c> at task 3.2 once a patch is
/// human-approved.
/// </summary>
/// <param name="Value">The underlying GUID.</param>
public readonly record struct GraphPatchId(Guid Value)
{
    /// <summary>Generate a fresh patch id (UUID v7, time-ordered).</summary>
    public static GraphPatchId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
