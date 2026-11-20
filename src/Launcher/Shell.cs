using System.Diagnostics;
using System.IO;

namespace Launcher;

/// <summary>Starts processes on the user's behalf. A seam so the view model can be tested without launching anything.</summary>
public interface IShell
{
    void StartApp(string executablePath);

    void OpenUrl(Uri url);

    void OpenFolder(string path);
}

internal sealed class ProcessShell : IShell
{
    public void StartApp(string executablePath) => Start(new ProcessStartInfo(executablePath)
    {
        WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        UseShellExecute = true
    });

    public void OpenUrl(Uri url) => Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

    public void OpenFolder(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // The launcher does not wait on or track the started process.
    private static void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();
}
