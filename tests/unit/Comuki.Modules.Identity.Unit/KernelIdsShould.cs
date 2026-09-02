using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Shared-kernel id factories: UUIDv7 minting and string round-trip for the
/// three cross-module identifiers that live outside Identity itself.
/// </summary>
public sealed class KernelIdsShould
{
    [Fact(DisplayName = "Given a fresh RunId, when New is called, then the value is version-7 and ToString matches Guid")]
    public void MintRunIdAsUuidV7()
    {
        var id = RunId.New();

        id.Value.Version.ShouldBe(7);
        id.ToString().ShouldBe(id.Value.ToString());
        id.ShouldNotBe(RunId.New());
    }

    [Fact(DisplayName = "Given a fresh WorkerId, when New is called, then the value is version-7 and ToString matches Guid")]
    public void MintWorkerIdAsUuidV7()
    {
        var id = WorkerId.New();

        id.Value.Version.ShouldBe(7);
        id.ToString().ShouldBe(id.Value.ToString());
        id.ShouldNotBe(WorkerId.New());
    }

    [Fact(DisplayName = "Given a fresh ProjectId, when New is called, then the value is version-7 and ToString matches Guid")]
    public void MintProjectIdAsUuidV7()
    {
        var id = ProjectId.New();

        id.Value.Version.ShouldBe(7);
        id.ToString().ShouldBe(id.Value.ToString());
        id.ShouldNotBe(ProjectId.New());
    }

    [Fact(DisplayName = "Given an explicit Guid, when wrapped in RunId, then Value and ToString round-trip")]
    public void WrapExplicitRunId()
    {
        var guid = Guid.CreateVersion7();
        var id = new RunId(guid);

        id.Value.ShouldBe(guid);
        id.ToString().ShouldBe(guid.ToString());
    }
}
