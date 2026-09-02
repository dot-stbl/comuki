using Comuki.Modules.Costs.Domain.Events;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Costs.Infrastructure.Persistence;

/// <summary>Enum ↔ wire-key converters for Costs columns.</summary>
public static class CostsKeyConverters
{
    /// <summary><see cref="UsageSource"/> ↔ key string.</summary>
    public static readonly ValueConverter<UsageSource, string> SourceToKey = new(
        static source => UsageSourceKeys.Of(source),
        static key => UsageSourceKeys.Parse(key));
}
