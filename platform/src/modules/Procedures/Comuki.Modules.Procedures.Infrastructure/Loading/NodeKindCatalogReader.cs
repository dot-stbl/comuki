using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Domain.Catalog;
using Comuki.Modules.Procedures.Domain.Exceptions;
using Comuki.Modules.Procedures.Domain.Loading;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Procedures.Infrastructure.Loading;

/// <summary>
/// File-backed reader: walks <c>{controlPlaneRoot}/{FolderName}/</c>, parses
/// every <c>.md</c> frontmatter document with <see cref="NodeKindDescriptorDocumentParser"/>,
/// and materialises the typed <see cref="NodeKindCatalog"/>. Validation that
/// the baseline v1 set ships complete is the catalog's responsibility —
/// this reader loads whatever is in the folder and refuses malformed
/// entries loudly. The baseline content lives at
/// <c>control-plane/procedure-node-kinds/*.md</c>.
/// </summary>
public sealed class NodeKindCatalogReader(
    ILogger<NodeKindCatalogReader> logger) : INodeKindCatalogReader
{
    /// <inheritdoc />
    public async Task<NodeKindCatalog> LoadAsync(
        string controlPlaneRoot,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(controlPlaneRoot, NodeKindCatalog.FolderName);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"Procedure-node-kind catalog folder '{folder}' does not exist.");
        }

        var entries = new List<NodeKindCatalogEntry>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filePath in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly))
        {
            var key = Path.GetFileNameWithoutExtension(filePath);
            var text = await File.ReadAllTextAsync(filePath, cancellationToken);
            var document = NodeKindDescriptorDocumentParser.Parse(text) ?? throw new ProcedureNodeKindsDomainException(
                    ProcedureNodeKindsDomainException.OwnerSurfaceMissing,
                    $"Procedure-node-kind descriptor '{filePath}' is malformed: missing key/title/description or frontmatter.");
            var descriptor = NodeKindCatalogMaterializer.Materialize(key, document);
            if (!seenKeys.Add(key))
            {
                throw new ProcedureNodeKindsDomainException(
                    ProcedureNodeKindsDomainException.DuplicateKey,
                    $"Procedure-node-kind descriptor key '{key}' appears more than once in the catalog.");
            }

            entries.Add(new NodeKindCatalogEntry(key, descriptor));
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        logger.LogInformation(
            "Loaded {Count} procedure-node-kind descriptors from {Folder}",
            entries.Count,
            folder);

        return new NodeKindCatalog(
            Version: NodeKindCatalog.BaselineVersion,
            SourceRef: NodeKindCatalogMaterializer.ResolveSourceRef(controlPlaneRoot),
            Entries: entries);
    }
}
