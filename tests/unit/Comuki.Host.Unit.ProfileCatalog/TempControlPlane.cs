using System.Text;
using Comuki.Host.ControlPlane;

namespace Comuki.Host.Unit.ProfileCatalog;

/// <summary>
/// Creates a throwaway control-plane root under the temp path. Each test gets
/// a unique tree; deleted on dispose.
/// </summary>
public sealed class TempControlPlane : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "comuki-catalog-" + Guid.NewGuid().ToString("N"));

    public void WriteProfile(string fileName, string content)
    {
        Write(ControlPlaneCatalog.ProfilesFolder, fileName, content);
    }

    public void WriteChatCommand(string fileName, string content)
    {
        Write(ControlPlaneCatalog.ChatCommandsFolder, fileName, content);
    }

    public void Write(string folderName, string fileName, string content)
    {
        var directory = Path.Combine(Root, folderName);
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
