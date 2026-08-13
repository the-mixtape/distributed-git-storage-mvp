using DistributedGitStorage.StorageNode.Models;
using DistributedGitStorage.StorageNode.Services;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGitStorage.StorageNode.Controllers;

[ApiController]
[Route("internal/repositories/{repositoryId:guid}")]
public sealed class RepositoriesController(GitRepositoryStore repositoryStore) : ControllerBase
{
    [HttpGet]
    public IActionResult Exists(Guid repositoryId) =>
        repositoryStore.Exists(repositoryId) ? Ok() : NotFound();

    [HttpGet("state")]
    public async Task<ActionResult<RepositoryStateResponse>> GetState(
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        var state = await repositoryStore.GetStateAsync(repositoryId, cancellationToken);
        return state.Exists ? Ok(state) : NotFound(state);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid repositoryId,
        CreateRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        await repositoryStore.CreateAsync(repositoryId, request.DefaultBranch, cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(Guid repositoryId)
    {
        await repositoryStore.DeleteAsync(repositoryId);
        return NoContent();
    }

    [HttpPost("replicate")]
    public async Task<IActionResult> Replicate(
        Guid repositoryId,
        ReplicateRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        await repositoryStore.ReplicateAsync(repositoryId, request.SourceUrl, cancellationToken);
        return NoContent();
    }
}
