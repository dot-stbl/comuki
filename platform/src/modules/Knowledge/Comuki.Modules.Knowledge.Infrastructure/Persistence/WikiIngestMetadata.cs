using Comuki.Modules.Knowledge.Domain;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Parsed Wiki metadata carried in the body of a <c>source = wiki</c>
/// ingest call. The <see cref="PgKnowledgeIngestor"/> extracts this from
/// the body's frontmatter (<c>kind</c> / <c>link_graph</c> /
/// <c>supersedes</c>) when the document is a Wiki page; for every other
/// kind the values stay null.
/// </summary>
/// <param name="Kind">Wiki page kind (glossary | how-to | decision-record | incident | reference); null when the frontmatter omits it.</param>
/// <param name="LinkGraph">Adjacency list parsed from the <c>link_graph</c> frontmatter entry; empty when absent.</param>
public sealed record WikiIngestMetadata(
    WikiPageKind? Kind,
    IReadOnlyList<WikiPageLink> LinkGraph);
