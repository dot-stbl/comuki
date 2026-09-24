// Ported from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — fidelity over house style.
namespace Comuki.Shared.Filtering.Unit.TestEntities;

/// <summary>
///     Test-double for the engine's domain smart-types — same shape as
///     <c>Comuki.Engine.Orchestration.Domain.RunStatus</c> (private constructor,
///     <c>readonly record struct</c>, public <see cref="FromWire" /> that throws on
///     unknown wire values). Lives in the shared Filtering test project so the
///     unit tests can exercise the smart-type branch without taking a dependency on
///     the engine module.
/// </summary>
public readonly record struct SampleSmartField
{
    private readonly string? value;

    private SampleSmartField(string value)
    {
        this.value = value;
    }

    /// <summary>Default placeholder — <c>default(SampleSmartField)</c>.</summary>
    public static SampleSmartField Unspecified { get; }

    /// <summary>First working value.</summary>
    public static SampleSmartField Wire1 { get; } = new("Wire1");

    /// <summary>Second working value.</summary>
    public static SampleSmartField Wire2 { get; } = new("Wire2");

    /// <summary>Third working value.</summary>
    public static SampleSmartField Wire3 { get; } = new("Wire3");

    /// <summary>Wire-form string — the exact PascalCase text the test serialises.</summary>
    public string Value => value ?? nameof(Unspecified);

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    /// <summary>Parse a stored wire-form string back into the smart-type; throws on unknown values.</summary>
    /// <param name="wire">The wire-form text to resolve, e.g. <c>"Wire1"</c>.</param>
    public static SampleSmartField FromWire(string wire)
    {
        return wire switch
        {
            nameof(Wire1) => Wire1,
            nameof(Wire2) => Wire2,
            nameof(Wire3) => Wire3,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, $"Unknown {nameof(SampleSmartField)} value."),
        };
    }
}
