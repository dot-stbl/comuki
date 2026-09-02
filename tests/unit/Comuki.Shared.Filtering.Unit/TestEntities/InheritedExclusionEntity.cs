// Ported from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — fidelity over house style.
using System.ComponentModel.DataAnnotations.Schema;

namespace Comuki.Shared.Filtering.Unit.TestEntities;

/// <summary>
///     Base of a TPH-shaped hierarchy whose discriminator is a computed CLR property:
///     abstract on the base, overridden per subtype, no backing field and no column.
/// </summary>
public abstract class InheritedExclusionBase
{
    /// <summary>Ordinary mapped field — must stay filterable in every subtype.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Computed discriminator; excluded here, and the exclusion must reach subtypes.</summary>
    [NotMapped]
    public abstract SampleStatus Kind { get; }

    /// <summary>
    ///     Mapped, ordinary type, hidden on the base with <c>[FilteredIgnore]</c>. The hiding
    ///     must reach the overriding subtype — that is the type a repository queries.
    /// </summary>
    [FilteredIgnore]
    public virtual string Secret { get; set; } = string.Empty;
}

/// <summary>Subtype that overrides both excluded properties without repeating the attributes.</summary>
public sealed class InheritedExclusionEntity : InheritedExclusionBase
{
    /// <inheritdoc />
    public override SampleStatus Kind => SampleStatus.Active;

    /// <inheritdoc />
    public override string Secret { get; set; } = string.Empty;
}
