namespace Comuki.Modules.Intake.Domain.Ids;

/// <summary>
/// Strong-typed identifier of an intake ticket — one seen external issue
/// inside one project scope.
/// </summary>
/// <param name="Value"></param>
public readonly record struct IncomingTicketId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns></returns>
    public static IncomingTicketId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
