using Comuki.Modules.Verify.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Verify.Unit;

/// <summary>
/// <see cref="OutputCapture"/> in isolation (via
/// <c>InternalsVisibleTo</c>): stream-order interleaving and the soft
/// character cap that drops the head and keeps the tail.
/// </summary>
public sealed class OutputCaptureShould
{
    [Fact(DisplayName = "Given stdout and stderr lines, when appended, then Snapshot preserves append order with source markers")]
    public void PreserveAppendOrder()
    {
        var capture = new OutputCapture(cap: 1024, NullLogger.Instance);

        capture.AppendLine("building", isError: false);
        capture.AppendLine("warning: unused variable", isError: true);
        capture.AppendLine("done", isError: false);

        capture.Snapshot().ShouldBe("[out] building\n[err] warning: unused variable\n[out] done\n");
    }

    [Fact(DisplayName = "Given a null line, when appended, then it is ignored")]
    public void IgnoreNullLine()
    {
        var capture = new OutputCapture(cap: 1024, NullLogger.Instance);

        capture.AppendLine(null, isError: false);

        capture.Snapshot().ShouldBe(string.Empty);
    }

    [Fact(DisplayName = "Given output past the cap, when Snapshot is read, then the head is dropped and the tail is kept with a truncation marker")]
    public void DropHeadPastCap()
    {
        // "[out] ABCDEFGHIJ\n" (the second appended line, markers
        // included) is exactly 17 chars — cap: 17 keeps that whole line
        // as the tail and drops everything before it, including the
        // first line, so the boundary lands on a marker, not mid-line.
        var capture = new OutputCapture(cap: 17, NullLogger.Instance);

        capture.AppendLine("0123456789", isError: false);
        capture.AppendLine("ABCDEFGHIJ", isError: false);

        var snapshot = capture.Snapshot();

        snapshot.ShouldBe("[truncated: head dropped]\n[out] ABCDEFGHIJ\n");
    }

    [Fact(DisplayName = "Given output under the cap, when Snapshot is read, then it is returned verbatim with no truncation marker")]
    public void NoTruncationUnderCap()
    {
        var capture = new OutputCapture(cap: 1024, NullLogger.Instance);

        capture.AppendLine("short", isError: false);

        capture.Snapshot().ShouldBe("[out] short\n");
    }
}
