using Comuki.Modules.Procedures.Application.Ports;
using Comuki.Modules.Procedures.Domain.Editions;

namespace Comuki.Modules.Procedures.Application.Editions;

/// <summary>
/// Community-edition default for <see cref="IEditionsFeatureSource"/>:
/// grants nothing beyond the free baseline. The paid editions host
/// (billing-driven feature resolution) replaces this registration when
/// it lands; until then every gated capability resolves to
/// "not granted" and the editions gate refuses it loudly instead of
/// silently succeeding.
/// </summary>
public sealed class CommunityEditionsFeatureSource() : IEditionsFeatureSource
{
    /// <inheritdoc />
    public GrantedFeatureKeys ResolveEffective()
    {
        return GrantedFeatureKeys.Empty;
    }
}
