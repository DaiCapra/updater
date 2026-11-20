namespace Launcher.Core;

public sealed record LauncherProfile(
    string DisplayName,
    string? BackgroundPath = null,
    UpdateSourceConfig? UpdateSource = null,
    Version? InstalledVersion = null)
{
    public static LauncherProfile Default { get; } = new("Launcher");
}
