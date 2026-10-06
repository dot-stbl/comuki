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

    /// <summary>The full <c>skills/</c> folder path inside the test tree.</summary>
    public string SkillsRoot => Path.Combine(Root, SkillCatalog.SkillsFolder);

    public void WriteProfile(string fileName, string content)
    {
        Write(ControlPlaneCatalog.ProfilesFolder, fileName, content);
    }

    public void WriteChatCommand(string fileName, string content)
    {
        Write(ControlPlaneCatalog.ChatCommandsFolder, fileName, content);
    }

    /// <summary>Writes a <c>SKILL.md</c> inside a <c>skills/&lt;dirName&gt;</c> subfolder.</summary>
    /// <param name="dirName">Skill directory name (the catalog key).</param>
    /// <param name="content">SKILL.md body — frontmatter + markdown body.</param>
    public void WriteSkill(string dirName, string content)
    {
        Directory.CreateDirectory(Path.Combine(SkillsRoot, dirName));
        File.WriteAllText(Path.Combine(SkillsRoot, dirName, SkillCatalog.SkillFileName), content, new UTF8Encoding(false));
    }

    public void Write(string folderName, string fileName, string content)
    {
        Directory.CreateDirectory(Path.Combine(Root, folderName));
        File.WriteAllText(Path.Combine(Root, folderName, fileName), content, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
