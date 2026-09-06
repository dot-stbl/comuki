namespace Comuki.Modules.Scheduler.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a <see cref="ScheduledJob"/>.
/// </summary>
/// <param name="Value"></param>
public readonly record struct ScheduledJobId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns></returns>
    public static ScheduledJobId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
