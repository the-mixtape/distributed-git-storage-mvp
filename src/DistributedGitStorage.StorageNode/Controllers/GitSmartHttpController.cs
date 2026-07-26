using DistributedGitStorage.StorageNode.Services;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGitStorage.StorageNode.Controllers;

[ApiController]
[Route("repositories/{repositoryId:guid}.git")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class GitSmartHttpController(GitRepositoryStore repositoryStore) : ControllerBase
{
    [HttpGet("info/refs")]
    public async Task GetInfoRefs(
        Guid repositoryId,
        [FromQuery] string service,
        CancellationToken cancellationToken)
    {
        Response.ContentType = service switch
        {
            "git-upload-pack" => "application/x-git-upload-pack-advertisement",
            "git-receive-pack" => "application/x-git-receive-pack-advertisement",
            _ => throw new BadHttpRequestException($"Unsupported Git service '{service}'.")
        };
        Response.Headers.CacheControl = "no-cache";
        await repositoryStore.AdvertiseRefsAsync(
            repositoryId,
            service,
            Request.Headers["Git-Protocol"].ToString(),
            Response.Body,
            cancellationToken);
    }

    [HttpPost("git-upload-pack")]
    public async Task UploadPack(Guid repositoryId, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-git-upload-pack-result";
        await repositoryStore.ExecuteRpcAsync(
            repositoryId,
            "git-upload-pack",
            Request.Headers["Git-Protocol"].ToString(),
            Request.Body,
            Response.Body,
            cancellationToken);
    }

    [HttpPost("git-receive-pack")]
    public async Task ReceivePack(Guid repositoryId, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-git-receive-pack-result";
        await repositoryStore.ExecuteRpcAsync(
            repositoryId,
            "git-receive-pack",
            Request.Headers["Git-Protocol"].ToString(),
            Request.Body,
            Response.Body,
            cancellationToken);
    }
}
