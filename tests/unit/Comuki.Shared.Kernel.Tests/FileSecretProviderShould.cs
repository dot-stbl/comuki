using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Kernel.Tests;

/// <summary>
/// FileSecretProvider tests — reads from a path under the <c>file</c>
/// scheme, trims trailing whitespace, enforces <see cref="FileSecretOptions.RootPath"/>
/// when configured. Uses temp files so the test does not depend on any
/// pre-existing host path. The temp dir is recreated per test instance
/// and left behind for the OS to clean up; per-test isolation matters
/// more than cleanup here.
/// </summary>
public sealed class FileSecretProviderShould
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), "comuki-secrets-tests-" + Guid.NewGuid().ToString("N"));

    [Fact(DisplayName = "Given an existing file with trailing newline, when ResolveAsync runs, then the trimmed value is returned")]
    public async Task ResolveAsyncReturnsTrimmedValueAsync()
    {
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "db-pass");
        File.WriteAllText(path, "secret-content\n");

        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions()));
        var resolved = await provider.ResolveAsync(new SecretRef("file", path, null), TestContext.Current.CancellationToken);

        resolved.ShouldBe("secret-content");
    }

    [Fact(DisplayName = "Given a missing file, when ResolveAsync runs, then null is returned (the resolver turns it into SecretRefUnsetException)")]
    public async Task ResolveAsyncReturnsNullForMissingFileAsync()
    {
        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions()));
        var path = Path.Combine(tempDir, "does-not-exist");

        var resolved = await provider.ResolveAsync(new SecretRef("file", path, null), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a file with a leading BOM and trailing whitespace, when ResolveAsync runs, then the trailing whitespace is trimmed (the BOM is auto-stripped by File.ReadAllText's UTF-8 decode)")]
    public async Task ResolveAsyncTrimsTrailingWhitespaceAsync()
    {
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "pass");
        // Raw bytes — the trailing whitespace is the part we assert on;
        // the leading BOM is consumed by File.ReadAllText's UTF-8 decode
        // (this is by design: callers never want the byte-3 EF BB BF in
        // their secret value), so the expected value does not include it.
        File.WriteAllBytes(path, "\uFEFFvalue-with-bom   \n\n"u8.ToArray());

        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions()));
        var resolved = await provider.ResolveAsync(new SecretRef("file", path, null), TestContext.Current.CancellationToken);

        resolved.ShouldBe("value-with-bom");
    }

    [Fact(DisplayName = "Given a provider, its Scheme is the lowercase 'file' string")]
    public void SchemeIsFile()
    {
        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions()));
        provider.Scheme.ShouldBe("file");
    }

    [Fact(DisplayName = "Given a RootPath allowlist and a ref path outside it, when ResolveAsync runs, then SecretRefFormatException is thrown before any filesystem read")]
    public async Task ResolveAsyncThrowsFormatExceptionForOutOfRootPathAsync()
    {
        Directory.CreateDirectory(tempDir);
        // The host's /etc/passwd / C:\Windows\... path is by construction
        // outside tempDir on every supported platform — both /etc/passwd
        // (Linux) and C:\etc\passwd (Windows when /etc/passwd is resolved
        // against the current drive) live outside the test's tempDir.
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", "outside-allowlist.txt"));
        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions { RootPath = tempDir }));

        var exception = await Should.ThrowAsync<SecretRefFormatException>(
            async () => await provider.ResolveAsync(new SecretRef("file", outsidePath, null), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(outsidePath);
        exception.Message.ShouldContain(tempDir);
    }

    [Fact(DisplayName = "Given a RootPath allowlist and a ref path inside it, when ResolveAsync runs, then the trimmed file contents are returned")]
    public async Task ResolveAsyncAllowsPathInsideRootAsync()
    {
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "db-pass");
        File.WriteAllText(path, "inside-root\n");

        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions { RootPath = tempDir }));
        var resolved = await provider.ResolveAsync(new SecretRef("file", path, null), TestContext.Current.CancellationToken);

        resolved.ShouldBe("inside-root");
    }

    [Fact(DisplayName = "Given no RootPath allowlist, when ResolveAsync runs against any path, then the allowlist is not enforced (dev-mode / bootstrap)")]
    public async Task ResolveAsyncWithoutRootPathAcceptsAnyPathAsync()
    {
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "anywhere");
        File.WriteAllText(path, "no-boundary\n");

        // A ref path is intentionally far outside tempDir to prove the
        // boundary is the options setting, not a structural check.
        var farAway = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", "..", "no-boundary.txt"));
        File.WriteAllText(farAway, "far-away\n");

        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions()));
        var resolved = await provider.ResolveAsync(new SecretRef("file", farAway, null), TestContext.Current.CancellationToken);

        resolved.ShouldBe("far-away");
    }

    [Fact(DisplayName = "Given a RootPath allowlist and a sibling dir with the same prefix, when ResolveAsync runs against the sibling, then SecretRefFormatException is thrown (prefix-overlap guard)")]
    public async Task ResolveAsyncRejectsSiblingWithSharedPrefixAsync()
    {
        Directory.CreateDirectory(tempDir);
        var siblingDir = tempDir + "-other";
        Directory.CreateDirectory(siblingDir);
        var siblingFile = Path.Combine(siblingDir, "pass");
        File.WriteAllText(siblingFile, "sibling\n");

        var provider = new FileSecretProvider(Options.Create(new FileSecretOptions { RootPath = tempDir }));

        await Should.ThrowAsync<SecretRefFormatException>(
            async () => await provider.ResolveAsync(new SecretRef("file", siblingFile, null), TestContext.Current.CancellationToken));
    }
}
