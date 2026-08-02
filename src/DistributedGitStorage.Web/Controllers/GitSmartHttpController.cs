using DistributedGitStorage.Services.Exceptions;
using DistributedGitStorage.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace DistributedGitStorage.Web.Controllers;

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
        try
        {
            Response.ContentType = "application/x-git-receive-pack-result";
            await gitService.ReceivePackAsync(
                name,
                Request.Headers["Git-Protocol"].ToString(),
                Request.Body,
                Response.Body,
                cancellationToken);
        }
        catch (WriteQuorumNotReachedException exception) when (!Response.HasStarted)
        {
            Response.Clear();
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/x-git-receive-pack-result";
            Response.Headers["Retry-After"] = "5";
            Response.Headers["X-Git-Repository-Id"] = exception.RepositoryId.ToString();
            Response.Headers["X-Git-Repository-Generation"] = exception.Generation.ToString();
            Response.Headers["X-Git-Write-Quorum"] =
                $"{exception.CurrentCopies}/{exception.RequiredCopies}";
            await WriteGitFatalAsync(Response.Body, exception.Message, cancellationToken);
        }
    }

    private static async Task WriteGitFatalAsync(
        Stream responseBody,
        string message,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes($"\u0003{message}\n");
        var length = Encoding.ASCII.GetBytes((payload.Length + 4).ToString("x4", CultureInfo.InvariantCulture));
        await responseBody.WriteAsync(length, cancellationToken);
        await responseBody.WriteAsync(payload, cancellationToken);
        await responseBody.WriteAsync("0000"u8.ToArray(), cancellationToken);
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
