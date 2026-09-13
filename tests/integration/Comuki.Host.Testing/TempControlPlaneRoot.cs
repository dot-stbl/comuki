using System.Text;

namespace Comuki.Host.Testing;

/// <summary>
/// Throwaway control-plane root directory an integration harness points
/// <c>ControlPlane:Root</c> at — one profile / chat-command file per
/// suite's need, deleted on dispose.
/// </summary>
/// <param name="suiteName">Short, lowercase suite tag folded into the temp directory name (e.g. <c>"auth"</c>, <c>"chat"</c>) — only for telling leftover directories apart if a crashed run leaves one behind.</param>
public sealed class TempControlPlaneRoot(string suiteName) : IDisposable
{
    /// <summary>The control-plane root — pass this as <c>ControlPlane:Root</c>.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"comuki-host-{suiteName}-" + Guid.NewGuid().ToString("N"));

    /// <summary>Writes one file under <paramref name="folderName"/>, creating it if missing.</summary>
    public void Write(string folderName, string fileName, string content)
    {
        var directory = Path.Combine(Root, folderName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content, new UTF8Encoding(false));
    }

    /// <summary>The one <c>restart</c> chat command the auth/chat/realtime suites merge into the slash catalog — content is never asserted on, only its presence.</summary>
    public void WriteDefaultChatCommand()
    {
        Write("chat-commands", "restart.md", """
            ---
            name: restart
            description: Restart the current run.
            ---

            Restart the current run now.
            """);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
