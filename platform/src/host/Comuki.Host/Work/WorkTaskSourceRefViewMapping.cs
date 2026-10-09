using Comuki.Modules.Work.Domain.Sources;

namespace Comuki.Host.Work;

public sealed partial record WorkTaskSourceRefView
{
    /// <summary>Project a domain <see cref="WorkTaskSourceRef"/> into the wire response.</summary>
    public static WorkTaskSourceRefView From(WorkTaskSourceRef source)
    {
        return new WorkTaskSourceRefView(
            Kind: source.Kind.Value,
            ExternalId: source.ExternalId,
            DisplayName: source.DisplayName);
    }
}
