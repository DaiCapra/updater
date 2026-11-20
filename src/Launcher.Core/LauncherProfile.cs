namespace Launcher.Core;

public sealed record LauncherProfile(
    string DisplayName,
    string? BackgroundPath = null,
    UpdateSourceConfig? UpdateSource = null,
    Version? InstalledVersion = null,
    string? InstallId = null,
    string? Executable = null)
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    public static LauncherProfile Default { get; } = new("Launcher");

    /// <summary>The per-app folder name under the user's local app data: <see cref="InstallId"/>, or the display name without invalid characters.</summary>
    public string InstallFolderName
    {
        get
        {
            if (InstallId is not null)
            {
                return InstallId;
            }

            var name = new string(DisplayName.Where(c => !InvalidNameChars.Contains(c)).ToArray()).Trim();
            return name.Length > 0 ? name : "Launcher";
        }
    }
}
