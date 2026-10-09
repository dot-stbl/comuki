namespace Comuki.Modules.Work.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a <see cref="WorkTask"/> — UUIDv7
/// generated client-side (so a freshly created Task sorts by creation
/// order in the queue and the inbox dedupe ledger can stand on the
/// <c>PK</c> without a separate <c>created_at</c> index). Wire form is
/// the underlying <see cref="Guid"/> string; EF stores it via
/// <c>HasConversion</c>.
/// </summary>
public readonly record struct WorkTaskId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns></returns>
    public static WorkTaskId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
