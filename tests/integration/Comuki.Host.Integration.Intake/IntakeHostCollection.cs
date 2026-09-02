using Xunit;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// One shared <see cref="HostIntakeServer"/> for every intake test
/// class — one container, one host, no parallel fixtures racing on the
/// shared hook-secret env var. Tests scope themselves by project id and
/// delivery ids, so a shared database stays clean.
/// </summary>
[CollectionDefinition(nameof(IntakeHostCollection))]
public sealed class IntakeHostCollection : ICollectionFixture<HostIntakeServer>;
