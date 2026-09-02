// Ported from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — fidelity over house style.
using System.ComponentModel.DataAnnotations.Schema;

namespace Comuki.Shared.Filtering.Unit.TestEntities;

/// <summary>
///     Test entity covering every CLR type the DSL supports. Public properties are
///     filterable by default; <see cref="SecretKey" /> is excluded via <c>[NotMapped]</c>.
/// </summary>
public sealed class SampleEntity
{
    /// <summary>Guid field — Eq, NotEq, In.</summary>
    public Guid Id { get; set; }

    /// <summary>String field — Eq, NotEq, Contains, StartsWith, EndsWith, IsNull, IsNotNull.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional nullable string for <c>?</c> / <c>!?</c> null tests.</summary>
    public string? Nickname { get; set; }

    /// <summary>String field used for substring tests.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Enum field — Eq, NotEq, In.</summary>
    public SampleStatus Status { get; set; }

    /// <summary>Int field — range + In.</summary>
    public int Age { get; set; }

    /// <summary>Nullable int for <c>?</c> / <c>!?</c> null tests against <see cref="Nullable{T}" />.</summary>
    public int? OptionalAge { get; set; }

    /// <summary>Long field — range + In.</summary>
    public long Score { get; set; }

    /// <summary>Decimal field — range + In.</summary>
    public decimal Balance { get; set; }

    /// <summary>Double field — range + In.</summary>
    public double Rating { get; set; }

    /// <summary>Bool field — Eq only.</summary>
    public bool IsActive { get; set; }

    /// <summary>DateTimeOffset field — range only.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>DateTime field — range only.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>TimeSpan field — range only.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Must be excluded — <c>[NotMapped]</c> opt-out.</summary>
    [NotMapped]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    ///     Must be excluded — <c>[FilteredIgnore]</c>. A mapped column of an ordinary,
    ///     perfectly filterable type: the only thing keeping it out of the DSL is the mark.
    /// </summary>
    [FilteredIgnore]
    public string Passphrase { get; set; } = string.Empty;

    /// <summary>Must be excluded — byte arrays map to <see cref="FilterOperator.None" />.</summary>
    public byte[]? Payload { get; set; }

    /// <summary>Must be excluded — object maps to <see cref="FilterOperator.None" />.</summary>
    public object? Bag { get; set; }
}

/// <summary>Test enum for the DSL enum-operator inference.</summary>
public enum SampleStatus
{
    /// <summary>Not active.</summary>
    Inactive = 0,

    /// <summary>Active.</summary>
    Active = 1,

    /// <summary>Archived (read-only).</summary>
    Archived = 2,

    /// <summary>Time-boxed trial.</summary>
    Trial = 3
}
