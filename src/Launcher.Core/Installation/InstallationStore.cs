using System.Text.Json;

namespace Launcher.Core.Installation;

public sealed record InstalledBuild(Version Version, string Directory);

/// <summary>
/// The on-disk layout for one app: <c>versions\&lt;version&gt;</c> holds builds, <c>downloads</c> holds staged
/// release files, and <c>installed.json</c> names the build that is currently active.
/// </summary>
public sealed class InstallationStore(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public string VersionsDirectory => Path.Combine(Root, "versions");

    public string DownloadsDirectory => Path.Combine(Root, "downloads");

    private string StatePath => Path.Combine(Root, "installed.json");

    /// <summary>The active build, or null when nothing is installed or the state file no longer matches the disk.</summary>
    public InstalledBuild? Current
    {
        get
        {
            try
            {
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath));
                if (state is not { Version: { } text, Folder: { } folder }
                    || !Version.TryParse(text, out var version)
                    || !PathSafety.IsPlainName(folder))
                {
                    return null;
                }

                var directory = Path.Combine(VersionsDirectory, folder);
                return Directory.Exists(directory) ? new InstalledBuild(version, directory) : null;
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Makes <c>versions\<paramref name="folder"/></c> the active build. The state file is replaced atomically.</summary>
    public void Activate(Version version, string folder)
    {
        Directory.CreateDirectory(Root);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new State(version.ToString(), folder)));
        File.Move(temp, StatePath, overwrite: true);
    }

    private sealed record State(string? Version, string? Folder);
}
