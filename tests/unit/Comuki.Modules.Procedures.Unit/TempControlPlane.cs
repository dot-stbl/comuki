using System.Text;

namespace Comuki.Modules.Procedures.Unit;

/// <summary>
/// Builds a throwaway control-plane tree under the temp path with one
/// <c>procedure-node-kinds/</c> folder so the catalog reader under test
/// loads from a real file path without touching the repo.
/// </summary>
public sealed class TempControlPlane : IDisposable
{
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(),
        "comuki-node-kinds-" + Guid.NewGuid().ToString("N"));

    public string KindsFolder => Path.Combine(
        Root,
        Domain.Catalog.NodeKindCatalog.FolderName);

    public TempControlPlane()
    {
        Directory.CreateDirectory(KindsFolder);
    }

    public void Write(string fileName, string content)
    {
        var path = Path.Combine(KindsFolder, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
