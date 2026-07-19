using GitalyControlPlane.Services.Options;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace GitalyControlPlane.Services.Services;

internal sealed class GitalyClientProvider : IDisposable
{
    public GitalyClientProvider(IOptions<GitalyOptions> options)
    {
        if (options.Value.Storages.Count == 0)
        {
            throw new InvalidOperationException("At least one Gitaly storage must be configured.");
        }

        Storages = options.Value.Storages
            .Select(storage => new StorageClient(storage, GrpcChannel.ForAddress(storage.Address)))
            .ToArray();
    }

    public IReadOnlyList<StorageClient> Storages { get; }

    public StorageClient GetStorage(string name) =>
        Storages.Single(item => item.Options.Name == name);

    public void Dispose()
    {
        foreach (var storage in Storages)
        {
            storage.Channel.Dispose();
        }
    }
}

internal sealed record StorageClient(GitalyStorageOptions Options, GrpcChannel Channel)
{
    public Gitaly.RepositoryService.RepositoryServiceClient RepositoryClient { get; } = new(Channel);
    public Gitaly.SmartHTTPService.SmartHTTPServiceClient SmartHttpClient { get; } = new(Channel);
}
