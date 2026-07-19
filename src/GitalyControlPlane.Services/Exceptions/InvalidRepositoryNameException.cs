namespace GitalyControlPlane.Services.Exceptions;

public sealed class InvalidRepositoryNameException
    : Exception
{
    public InvalidRepositoryNameException()
        : base("Repository name must be 1-100 characters and contain only letters, digits, '.', '_' or '-'.")
    {
    }
}
