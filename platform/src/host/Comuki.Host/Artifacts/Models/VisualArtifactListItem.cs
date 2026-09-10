namespace Comuki.Host.Artifacts.Models;

/// <summary>One row in the visual-artifacts list — metadata only, no body.</summary>
public sealed record VisualArtifactListItem(
    Guid Id,
    string Filename,
    string ContentType,
    long SizeBytes,
    string? Title,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    Guid? RunId,
    Guid? WorkItemId,
    Guid? SessionId,
    Guid? TicketId,
    int Version);
