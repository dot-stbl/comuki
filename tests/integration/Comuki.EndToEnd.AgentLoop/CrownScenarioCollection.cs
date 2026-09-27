using Xunit;

namespace Comuki.EndToEnd.AgentLoop;

/// <summary>
/// One shared <see cref="CrownScenarioHost"/> for the WS10 crown suite — one
/// host, one Postgres; sequential so the two facts never race on the shared
/// webhook-secret env var or on the shared Postgres schema state.
/// </summary>
[CollectionDefinition(nameof(CrownScenarioCollection), DisableParallelization = true)]
public sealed class CrownScenarioCollection : ICollectionFixture<CrownScenarioHost>;
