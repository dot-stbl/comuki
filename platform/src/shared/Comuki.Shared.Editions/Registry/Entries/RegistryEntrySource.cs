namespace Comuki.Shared.Editions.Registry.Entries;

/// <summary>
/// Which catalog a <see cref="RegistryEntry"/> was built from. A smart
/// type, not an enum (issue #164 review — an enum is a wire/EF-boundary
/// form; this discriminator never leaves the process in this chunk, so
/// the domain rule in <c>anti-patterns.md</c> §1.4 / <c>smart-types.md</c>
/// §2 applies).
/// </summary>
public readonly record struct RegistryEntrySource
{
    /// <summary>
    /// The default value (<c>default(RegistryEntrySource)</c>). Never a
    /// legal source for an actual <see cref="RegistryEntry"/> — observing
    /// it is a data-invariant violation, not a silent third option.
    /// </summary>
    public static RegistryEntrySource Unspecified { get; }

    /// <summary>Built from <see cref="Editions.Features"/>.</summary>
    public static RegistryEntrySource Feature { get; } = new("feature");

    /// <summary>Built from <see cref="Editions.Limits"/>.</summary>
    public static RegistryEntrySource Limit { get; } = new("limit");

    private readonly string? value;

    private RegistryEntrySource(string value) => this.value = value;

    /// <summary>Lowercase string form; <see cref="Unspecified"/> is <c>"unspecified"</c>.</summary>
    public string Value => value ?? "unspecified";
}
