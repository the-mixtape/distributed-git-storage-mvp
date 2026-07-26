using DistributedGitStorage.Services.Interfaces;
using DistributedGitStorage.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGitStorage.Web.Controllers;

[ApiController]
[Route("repositories")]
public sealed class RepositoriesController(IRepositoryService repositoryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RepositoryInfo>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RepositoryInfo>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await repositoryService.GetRepositoriesAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RepositoryInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepositoryInfo>> Get(Guid id, CancellationToken cancellationToken)
    {
        var repository = await repositoryService.GetAsync(id, cancellationToken);
        return repository is null ? NotFound() : Ok(repository);
    }

    [HttpPost]
    [ProducesResponseType<RepositoryInfo>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RepositoryInfo>> Create(
        CreateRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        var repository = await repositoryService.CreateAsync(request.Name, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = repository.Id }, repository);
    }
}
