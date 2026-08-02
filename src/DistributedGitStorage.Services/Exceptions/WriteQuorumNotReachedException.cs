namespace DistributedGitStorage.Services.Exceptions;

public sealed class WriteQuorumNotReachedException(
    Guid repositoryId,
    long generation,
    int currentCopies,
    int requiredCopies)
    : Exception(
        $"Write quorum was not reached for repository '{repositoryId}', generation {generation}: "
        + $"{currentCopies} of {requiredCopies} required copies are current. "
        + "The primary may already contain the pushed refs; check replica state or retry the push.")
{
    public Guid RepositoryId { get; } = repositoryId;
    public long Generation { get; } = generation;
    public int CurrentCopies { get; } = currentCopies;
    public int RequiredCopies { get; } = requiredCopies;
}
