using GitalyControlPlane.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GitalyControlPlane.Web.Controllers;

[ApiController]
[Route("{name}.git")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class GitSmartHttpController(IGitSmartHttpService gitService) : ControllerBase
{
    [HttpGet("info/refs")]
    public async Task GetInfoRefs(
        string name,
        [FromQuery] string service,
        CancellationToken cancellationToken)
    {
        Response.ContentType = service switch
        {
            "git-upload-pack" => "application/x-git-upload-pack-advertisement",
            "git-receive-pack" => "application/x-git-receive-pack-advertisement",
            _ => throw new BadHttpRequestException("Unsupported Git service.")
        };

        await gitService.WriteInfoRefsAsync(
            name,
            service,
            Request.Headers["Git-Protocol"].ToString(),
            Response.Body,
            cancellationToken);
    }

    [HttpPost("git-receive-pack")]
    [Consumes("application/x-git-receive-pack-request")]
    public async Task ReceivePack(string name, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-git-receive-pack-result";
        await gitService.ReceivePackAsync(
            name,
            Request.Headers["Git-Protocol"].ToString(),
            Request.Body,
            Response.Body,
            cancellationToken);
    }

    [HttpPost("git-upload-pack")]
    [Consumes("application/x-git-upload-pack-request")]
    public async Task UploadPack(string name, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-git-upload-pack-result";
        await gitService.UploadPackAsync(
            name,
            Request.Headers["Git-Protocol"].ToString(),
            Request.Body,
            Response.Body,
            cancellationToken);
    }
}
