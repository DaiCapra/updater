namespace Launcher.Tests.Fakes;

/// <summary>Records what the launcher would have started instead of starting it.</summary>
internal sealed class FakeShell : IShell
{
    public List<string> Apps { get; } = [];
    public List<Uri> Urls { get; } = [];
    public List<string> Folders { get; } = [];

    public void StartApp(string executablePath) => Apps.Add(executablePath);

    public void OpenUrl(Uri url) => Urls.Add(url);

    public void OpenFolder(string path) => Folders.Add(path);
}
