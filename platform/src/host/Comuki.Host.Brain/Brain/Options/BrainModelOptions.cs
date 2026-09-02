namespace Comuki.Host.Brain.Brain.Options;

/// <summary>The leading model connection — any OpenAI-compatible endpoint.</summary>
public sealed record BrainModelOptions
{
    /// <summary>OpenAI-compatible base endpoint (e.g. the provider's v4 API root); null = unconfigured.</summary>
    public string? Endpoint { get; init; }

    /// <summary>API key; read from env in deployments. Null = unconfigured.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Model id the requests name.</summary>
    public string? ModelId { get; init; }

    /// <summary>True when endpoint, key and model id are all present.</summary>
    public bool IsConfigured => Endpoint is not null && ApiKey is not null && ModelId is not null;
}
