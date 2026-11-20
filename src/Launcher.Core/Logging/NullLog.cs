namespace Launcher.Core.Logging;

/// <summary>Discards everything; the default when no log is configured.</summary>
public sealed class NullLog : ILauncherLog
{
    public static NullLog Instance { get; } = new();

    public void Write(LogLevel level, string message, Exception? error = null)
    {
    }
}
