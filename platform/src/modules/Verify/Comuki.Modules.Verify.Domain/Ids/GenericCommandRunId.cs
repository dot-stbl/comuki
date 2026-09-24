namespace Comuki.Modules.Verify.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a <see cref="Runs.GenericCommandRun"/>.
/// UUIDv7 so the runner-poll query benefits from the monotonic order.
/// </summary>
/// <param name="Value">The raw UUIDv7.</param>
public readonly record struct GenericCommandRunId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns>A fresh id.</returns>
    public static GenericCommandRunId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
