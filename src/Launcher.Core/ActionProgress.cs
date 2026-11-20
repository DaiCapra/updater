namespace Launcher.Core;

/// <summary>
/// Reports each value by calling <paramref name="report"/> on the reporting thread. Unlike <see cref="Progress{T}"/>,
/// it never posts to a captured synchronization context, so the caller decides which thread to marshal to.
/// </summary>
public sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
