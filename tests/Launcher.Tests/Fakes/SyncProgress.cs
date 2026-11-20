namespace Launcher.Tests.Fakes;

/// <summary>An <see cref="IProgress{T}"/> that hands each report straight to a callback on the calling thread.</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
