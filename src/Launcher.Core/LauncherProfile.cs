namespace Launcher.Core;

public sealed record LauncherProfile(
    string DisplayName,
    string? BackgroundPath = null,
    Uri? UpdateFeedUrl = null,
    Version? InstalledVersion = null)
{
    public static LauncherProfile Default { get; } = new("Launcher");
}
