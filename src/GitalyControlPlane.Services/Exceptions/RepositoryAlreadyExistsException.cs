namespace GitalyControlPlane.Services.Exceptions;

public sealed class RepositoryAlreadyExistsException(string name, Exception? innerException = null)
    : Exception($"Repository '{name}' already exists.", innerException);
