namespace Comuki.Modules.Artifacts.Domain.VisualArtifacts;

/// <summary>
/// Strong-typed wrapper for the artifact row's id — keeps callers from
/// accidentally passing the wrong <see cref="Guid"/> to the store API.
/// </summary>
/// <param name="Value">Underlying UUIDv7.</param>
public readonly record struct VisualArtifactId(Guid Value)
{
    /// <summary>Mints a new UUIDv7 artifact id.</summary>
    public static VisualArtifactId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
