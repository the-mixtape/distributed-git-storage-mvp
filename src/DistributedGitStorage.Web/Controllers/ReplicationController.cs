using DistributedGitStorage.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGitStorage.Web.Controllers;

[ApiController]
[Route("replication/jobs")]
public sealed class ReplicationController(IReplicationService replicationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReplicationJobInfo>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await replicationService.GetJobsAsync(cancellationToken));

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await replicationService.RetryAsync(id, cancellationToken);
        return Accepted();
    }
}
