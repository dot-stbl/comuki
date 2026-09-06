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
}
