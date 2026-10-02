namespace Comuki.Modules.Procedures.Domain.Kinds.Types;

/// <summary>
/// What the compile gate requires of a state-changing kind on idempotency.
/// <see cref="Optional"/> kinds may run without a declared idempotency key;
/// <see cref="Required"/> kinds must declare one or publication is refused;
/// <see cref="Inherent"/> kinds are naturally idempotent (a read, a verify,
/// a join).
/// </summary>
public readonly record struct NodeKindIdempotency
{
    private readonly string? value;

    private NodeKindIdempotency(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — not a valid idempotency declaration.</summary>
    public static NodeKindIdempotency Unspecified { get; }

    /// <summary>No idempotency requirement; may omit an idempotency key.</summary>
    public static NodeKindIdempotency Optional { get; } = new(nameof(Optional));

    /// <summary>An idempotency key must be declared at compile time.</summary>
    public static NodeKindIdempotency Required { get; } = new(nameof(Required));

    /// <summary>Idempotent by construction (a read or a verify needs no key).</summary>
    public static NodeKindIdempotency Inherent { get; } = new(nameof(Inherent));

    /// <summary>Wire-form string.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>
    /// Parse a wire-form string; case-insensitive (frontmatter writers use
    /// lowercase by convention, PascalCase is the canonical wire form).
    /// Throws on unknown values.
    /// </summary>
    /// <param name="wire"></param>
    // TODO(SMART-TYPES): убрать после [SmartType]-генератора — см. source-generators.md §1
    public static NodeKindIdempotency FromWire(string wire)
    {
        // A switch expression with when-clauses keeps the case-insensitive
        // intent visible without a long if-else chain.
        return wire switch
        {
            var w when string.Equals(w, nameof(Optional), StringComparison.OrdinalIgnoreCase) => Optional,
            var w when string.Equals(w, nameof(Required), StringComparison.OrdinalIgnoreCase) => Required,
            var w when string.Equals(w, nameof(Inherent), StringComparison.OrdinalIgnoreCase) => Inherent,
            _ => throw new ArgumentOutOfRangeException(
                nameof(wire),
                wire,
                $"Unknown {nameof(NodeKindIdempotency)} value."),
        };
    }
}
