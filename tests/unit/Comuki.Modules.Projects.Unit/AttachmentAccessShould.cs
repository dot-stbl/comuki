using Comuki.Modules.Projects.Domain.Attachments;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Closed set of <see cref="AttachmentAccess"/> — the declared access
/// level carried by an attachment (effective access is resolved by the
/// Repositories module from this and the credential's default; the
/// Projects side only carries the declaration). PascalCase wire form so
/// EF stores the same identifier the JSON contract surfaces.
/// </summary>
public sealed class AttachmentAccessShould
{
    [Fact(DisplayName = "Given the closed set, when All is enumerated, then every working access is present and the placeholder is excluded")]
    public void EnumerateAllWorkingAccesses()
    {
        AttachmentAccess.All.ShouldBe(
            [AttachmentAccess.External, AttachmentAccess.Read, AttachmentAccess.Write]);
    }

    [Fact(DisplayName = "Given the default placeholder, when Value is read, then the wire-form text is the type name")]
    public void DefaultPlaceholderReadsAsUnspecified()
    {
        AttachmentAccess.Unspecified.Value.ShouldBe(nameof(AttachmentAccess.Unspecified));
    }

    [Fact(DisplayName = "Given External access, when Value is read, then it is the PascalCase wire form")]
    public void ExternalValueIsPascalCase()
    {
        AttachmentAccess.External.Value.ShouldBe("External");
    }

    [Fact(DisplayName = "Given Read access, when Value is read, then it is the PascalCase wire form")]
    public void ReadValueIsPascalCase()
    {
        AttachmentAccess.Read.Value.ShouldBe("Read");
    }

    [Fact(DisplayName = "Given Write access, when Value is read, then it is the PascalCase wire form")]
    public void WriteValueIsPascalCase()
    {
        AttachmentAccess.Write.Value.ShouldBe("Write");
    }

    [Fact(DisplayName = "Given the External wire form, when FromWire is called, then External is returned")]
    public void ParseExternal()
    {
        AttachmentAccess.FromWire("External").ShouldBe(AttachmentAccess.External);
    }

    [Fact(DisplayName = "Given the Read wire form, when FromWire is called, then Read is returned")]
    public void ParseRead()
    {
        AttachmentAccess.FromWire("Read").ShouldBe(AttachmentAccess.Read);
    }

    [Fact(DisplayName = "Given the Write wire form, when FromWire is called, then Write is returned")]
    public void ParseWrite()
    {
        AttachmentAccess.FromWire("Write").ShouldBe(AttachmentAccess.Write);
    }

    [Fact(DisplayName = "Given an unknown wire value, when FromWire is called, then it throws ArgumentOutOfRangeException")]
    public void RejectUnknownWireValue()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => AttachmentAccess.FromWire("owner"));
    }
}
