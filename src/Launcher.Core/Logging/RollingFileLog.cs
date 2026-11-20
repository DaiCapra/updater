namespace Launcher.Core.Logging;

/// <summary>
/// Appends to <c>&lt;directory&gt;\launcher.log</c>. When the file would grow past <paramref name="maxBytes"/>, it rotates:
/// launcher.log → launcher.1.log → … → launcher.<c>keepFiles-1</c>.log, and the oldest is deleted.
/// Logging never throws, so a full disk or locked file can't break the launcher.
/// </summary>
public sealed class RollingFileLog(string directory, long maxBytes = 1024 * 1024, int keepFiles = 5, TimeProvider? clock = null)
    : ILauncherLog
{
    private readonly object gate = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public string CurrentPath => PathAt(0);

    public void Write(LogLevel level, string message, Exception? error = null)
    {
        var text = $"{clock.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}";
        if (error is not null)
        {
            text += Environment.NewLine + error;
        }

        var line = text + Environment.NewLine;
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(CurrentPath) && new FileInfo(CurrentPath).Length + line.Length > maxBytes)
                {
                    Rotate();
                }

                File.AppendAllText(CurrentPath, line);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Shifts every existing file up one slot, dropping the oldest.</summary>
    private void Rotate()
    {
        File.Delete(PathAt(keepFiles - 1));
        for (var index = keepFiles - 2; index >= 0; index--)
        {
            if (File.Exists(PathAt(index)))
            {
                File.Move(PathAt(index), PathAt(index + 1));
            }
        }
    }

    private string PathAt(int index) => Path.Combine(directory, index == 0 ? "launcher.log" : $"launcher.{index}.log");
}
