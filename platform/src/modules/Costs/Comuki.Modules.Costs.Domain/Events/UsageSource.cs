namespace Comuki.Modules.Costs.Domain.Events;

/// <summary>Who reported the usage — wire keys live in <see cref="UsageSourceKeys"/>.</summary>
public enum UsageSource
{
    /// <summary>Model gateway (YARP proxy).</summary>
    Proxy = 0,

    /// <summary>Brain host (leading model).</summary>
    Brain = 1,

    /// <summary>Worker / translator report.</summary>
    Worker = 2,

    /// <summary>Manual / test / system injection.</summary>
    System = 3,
}
