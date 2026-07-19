using System.Net.Http.Headers;
using GitalyControlPlane.Data;
using GitalyControlPlane.Data.Models;
using GitalyControlPlane.Services.Exceptions;
using GitalyControlPlane.Services.Interfaces;
using GitalyControlPlane.Services.Options;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitalyControlPlane.Services.Services;

internal sealed class GitalyGitSmartHttpService : IGitSmartHttpService
{
    private readonly GitalyClientProvider _clients;
    private readonly IDbContextFactory<GitalyControlPlaneDbContext> _dbContextFactory;
    private readonly HttpClient _sidechannelGateway;

    public GitalyGitSmartHttpService(GitalyClientProvider clients, IDbContextFactory<GitalyControlPlaneDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory, IOptions<GitalyOptions> options)
    {
        _clients = clients;
        _dbContextFactory = dbContextFactory;
        _sidechannelGateway = httpClientFactory.CreateClient("SidechannelGateway");
        _sidechannelGateway.BaseAddress = new Uri(options.Value.SidechannelGatewayAddress);
    }

    public async Task WriteInfoRefsAsync(string name, string service, string gitProtocol, Stream responseBody, CancellationToken cancellationToken)
    {
        var placement = await GetRequiredPlacementAsync(name, cancellationToken);
        var storage = _clients.GetStorage(placement.Storage);
        var request = new Gitaly.InfoRefsRequest { Repository = GitalyRepositoryService.CreateGitalyRepository(placement), GitProtocol = gitProtocol };
        AsyncServerStreamingCall<Gitaly.InfoRefsResponse> call = service switch
        {
            "git-upload-pack" => storage.SmartHttpClient.InfoRefsUploadPack(request, cancellationToken: cancellationToken),
            "git-receive-pack" => storage.SmartHttpClient.InfoRefsReceivePack(request, cancellationToken: cancellationToken),
            _ => throw new ArgumentException("Unsupported Git service.")
        };
        using (call)
        {
            await foreach (var chunk in call.ResponseStream.ReadAllAsync(cancellationToken))
                await responseBody.WriteAsync(chunk.Data.Memory, cancellationToken);
        }
    }

    public async Task ReceivePackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken)
    {
        var placement = await GetRequiredPlacementAsync(name, cancellationToken);
        var storage = _clients.GetStorage(placement.Storage);
        using var call = storage.SmartHttpClient.PostReceivePack(cancellationToken: cancellationToken);
        var responseTask = CopyResponseAsync(call.ResponseStream, responseBody, cancellationToken);
        await call.RequestStream.WriteAsync(new Gitaly.PostReceivePackRequest
        {
            Repository = GitalyRepositoryService.CreateGitalyRepository(placement), GlId = "poc-user",
            GlRepository = $"project-{placement.Id:N}", GlUsername = "poc-user", GitProtocol = gitProtocol
        }, cancellationToken);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await requestBody.ReadAsync(buffer, cancellationToken)) > 0)
            await call.RequestStream.WriteAsync(new Gitaly.PostReceivePackRequest { Data = ByteString.CopyFrom(buffer, 0, read) }, cancellationToken);
        await call.RequestStream.CompleteAsync();
        await responseTask;
    }

    public async Task UploadPackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken)
    {
        var placement = await GetRequiredPlacementAsync(name, cancellationToken);
        var storage = _clients.GetStorage(placement.Storage);
        var url = $"/upload-pack?address={Uri.EscapeDataString(storage.Options.SidechannelAddress)}" +
                  $"&storage={Uri.EscapeDataString(placement.StorageName)}&path={Uri.EscapeDataString(placement.RelativePath)}" +
                  $"&protocol={Uri.EscapeDataString(gitProtocol)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StreamContent(requestBody) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-git-upload-pack-request");
        using var response = await _sidechannelGateway.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(responseBody, cancellationToken);
    }

    private async Task<RepositoryPlacement> GetRequiredPlacementAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = GitalyRepositoryService.NormalizeName(name);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RepositoryPlacements.AsNoTracking().SingleOrDefaultAsync(x => x.Name == normalizedName, cancellationToken)
               ?? throw new RepositoryNotFoundException(name);
    }

    private static async Task CopyResponseAsync(IAsyncStreamReader<Gitaly.PostReceivePackResponse> stream, Stream target, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.ReadAllAsync(cancellationToken))
        {
            await target.WriteAsync(chunk.Data.Memory, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }
    }
}
