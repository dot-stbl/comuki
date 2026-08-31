namespace Comuki.Host.ControlPlane.Parsing;

/// <summary>A parsed control-plane document: frontmatter metadata plus the markdown body.</summary>
/// <param name="Name">Frontmatter name; the identity inside the document.</param>
/// <param name="Description">One-line description for catalogs and UI.</param>
/// <param name="AllowedTools">Tools the profile's workers may use; empty when the document does not carry the key.</param>
/// <param name="Model">Optional model role hint (e.g. light/heavy); null when absent.</param>
/// <param name="Body">Markdown after the closing fence - the system prompt or the command instructions.</param>
public sealed record ControlPlaneDocument(
    string Name,
    string Description,
    IReadOnlyList<string> AllowedTools,
    string? Model,
    string Body);
