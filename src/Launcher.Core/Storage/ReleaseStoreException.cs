namespace Launcher.Core.Storage;

public enum ReleaseStoreErrorKind
{
    AccessDenied,
    NotFound,
    Unavailable
}

/// <summary>
/// A store failure with a message the UI can show. Providers translate their SDK errors into this type
/// so callers never depend on a specific host's exceptions.
/// </summary>
public sealed class ReleaseStoreException(ReleaseStoreErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ReleaseStoreErrorKind Kind { get; } = kind;
}
