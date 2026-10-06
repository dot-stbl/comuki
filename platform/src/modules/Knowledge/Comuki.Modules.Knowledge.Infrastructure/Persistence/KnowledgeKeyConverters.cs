using Comuki.Modules.Knowledge.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the wire-key enums to their kebab-case string
/// columns — the same keys the brain/chat tool surface speaks.
/// </summary>
public static class KnowledgeKeyConverters
{
    /// <summary><see cref="SourceKind"/> ↔ kind key.</summary>
    public static readonly ValueConverter<SourceKind, string> SourceKindToKey = new(
        static kind => SourceKindKeys.Key(kind),
        static key => SourceKindKeys.ParseRequired(key));

    /// <summary><see cref="WikiPageKind"/> ↔ kind key (nullable: only Wiki documents carry a value).</summary>
    public static readonly ValueConverter<WikiPageKind?, string?> NullableWikiPageKindToKey = new(
        static kind => kind.HasValue ? WikiPageKindKeys.Key(kind.Value) : null,
        static key => string.IsNullOrEmpty(key) ? null : WikiPageKindKeys.ParseRequired(key));

    /// <summary><see cref="WikiPageLinkKind"/> ↔ wire key (used on owned link-graph elements).</summary>
    public static readonly ValueConverter<WikiPageLinkKind, string> WikiPageLinkKindToKey = new(
        static kind => WikiPageLinkKindKeys.Key(kind),
        static key => WikiPageLinkKindKeys.ParseRequired(key));
}
