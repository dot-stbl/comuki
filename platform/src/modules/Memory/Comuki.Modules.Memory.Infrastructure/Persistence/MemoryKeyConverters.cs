using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Learning;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Memory.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the wire-key enums to their kebab-case string
/// columns — the same keys the brain/chat tool surface speaks.
/// </summary>
public static class MemoryKeyConverters
{
    /// <summary><see cref="MemoryScope"/> ↔ scope key.</summary>
    public static readonly ValueConverter<MemoryScope, string> ScopeToKey = new(
        static scope => MemoryScopeKeys.Key(scope),
        static key => MemoryScopeKeys.ParseRequired(key));

    /// <summary><see cref="MemoryFactKind"/> ↔ kind key.</summary>
    public static readonly ValueConverter<MemoryFactKind, string> KindToKey = new(
        static kind => MemoryFactKindKeys.Key(kind),
        static key => MemoryFactKindKeys.ParseRequired(key));

    /// <summary><see cref="MemorySource"/> ↔ source key.</summary>
    public static readonly ValueConverter<MemorySource, string> SourceToKey = new(
        static source => MemorySourceKeys.Key(source),
        static key => MemorySourceKeys.ParseRequired(key));

    /// <summary><see cref="LearningStatus"/> ↔ status key.</summary>
    public static readonly ValueConverter<LearningStatus, string> LearningStatusToKey = new(
        static status => LearningStatusKeys.Key(status),
        static key => LearningStatusKeys.ParseRequired(key));
}
