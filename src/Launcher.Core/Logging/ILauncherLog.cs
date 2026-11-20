namespace Launcher.Core.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public interface ILauncherLog
{
    void Write(LogLevel level, string message, Exception? error = null);
}

public static class LauncherLogExtensions
{
    public static void Info(this ILauncherLog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warning(this ILauncherLog log, string message, Exception? error = null) => log.Write(LogLevel.Warning, message, error);

    public static void Error(this ILauncherLog log, string message, Exception? error = null) => log.Write(LogLevel.Error, message, error);
}
