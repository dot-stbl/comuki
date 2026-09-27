using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Repositories.Unit;

/// <summary>
/// Domain invariants of <see cref="RepositoryCredentialRef"/>:
/// <see cref="RepositoryAccess.Unspecified"/> and
/// <see cref="RepositoryAccess.External"/> are rejected as
/// <see cref="RepositoryCredentialRef.DefaultAccess"/>; only Read/Write are
/// legal defaults. <see cref="RepositoryCredentialRef.EffectiveAccess"/>
/// composes the lattice meet against an attachment access.
/// </summary>
public sealed class RepositoryCredentialRefShould
{
    private readonly DateTimeOffset now = new(2026, 9, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given Read as default access, when Create is called, then the credential is created")]
    public void AcceptReadAsDefault()
    {
        var credential = RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration-read", RepositoryAccess.Read, now);

        credential.DefaultAccess.ShouldBe(RepositoryAccess.Read);
        credential.IntegrationRef.ShouldBe("integration-read");
    }

    [Fact(DisplayName = "Given Write as default access, when Create is called, then the credential is created")]
    public void AcceptWriteAsDefault()
    {
        var credential = RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration-write", RepositoryAccess.Write, now);

        credential.DefaultAccess.ShouldBe(RepositoryAccess.Write);
    }

    [Fact(DisplayName = "Given Unspecified as default access, when Create is called, then the typed exception fires")]
    public void RejectUnspecifiedAsDefault()
    {
        var exception = Should.Throw<RepositoryDomainException>(() =>
            RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration", RepositoryAccess.Unspecified, now));

        exception.Code.ShouldBe(RepositoryDomainException.DefaultAccessInvalid);
    }

    [Fact(DisplayName = "Given External as default access, when Create is called, then the typed exception fires")]
    public void RejectExternalAsDefault()
    {
        var exception = Should.Throw<RepositoryDomainException>(() =>
            RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration", RepositoryAccess.External, now));

        exception.Code.ShouldBe(RepositoryDomainException.DefaultAccessInvalid);
    }

    [Fact(DisplayName = "Given a credential with Write as default, when EffectiveAccess is asked with a Read attachment, then it returns Read")]
    public void EffectiveAccessTakesTheMoreRestrictive()
    {
        var credential = RepositoryCredentialRef.Create(RepositoryIdForTest(), "integration", RepositoryAccess.Write, now);

        credential.EffectiveAccess(RepositoryAccess.Read).ShouldBe(RepositoryAccess.Read);
        credential.EffectiveAccess(RepositoryAccess.Write).ShouldBe(RepositoryAccess.Write);
        credential.EffectiveAccess(RepositoryAccess.External).ShouldBe(RepositoryAccess.External);
    }

    private static RepositoryId RepositoryIdForTest()
    {
        return RepositoryId.New();
    }
}
