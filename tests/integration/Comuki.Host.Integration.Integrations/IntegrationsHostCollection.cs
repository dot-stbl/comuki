using Xunit;

namespace Comuki.Host.Integration.Integrations;

/// <summary>
/// One shared <see cref="HostIntegrationServer"/> for every integrations test
/// class — one container, one host, no parallel fixtures racing on the
/// shared hook-secret env var. Tests scope themselves by project id and
/// delivery ids, so a shared database stays clean.
/// </summary>
/// <remarks>
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/>
/// is set because the integrations module's
/// EF Core change-tracker has pre-existing bugs (detached entity on
/// background worker writes; tracked-conflict under simultaneous
/// writes) that surface when several test classes hit the same
/// shared host at the same instant. Sequential execution removes the
/// race and keeps the suite green — the in-app scenarios this would
/// affect are out of scope for the admin slice.
/// </remarks>
[CollectionDefinition(nameof(IntegrationsHostCollection), DisableParallelization = true)]
public sealed class IntegrationsHostCollection : ICollectionFixture<HostIntegrationServer>;
