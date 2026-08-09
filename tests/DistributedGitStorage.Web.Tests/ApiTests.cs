using System.Net;
using System.Net.Http.Json;
using DistributedGitStorage.Services.Exceptions;
using DistributedGitStorage.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DistributedGitStorage.Web.Tests;

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetRepositories_ReturnsRepositories()
    {
        var repositories = await _client.GetFromJsonAsync<RepositoryInfo[]>("/repositories");
        var repository = Assert.Single(repositories!);
        Assert.Equal("demo", repository.Name);
    }

    [Fact]
    public async Task CreateRepository_ReturnsCreated()
    {
        using var response = await _client.PostAsJsonAsync("/repositories", new { name = "new-repo" });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateRepository_WithInvalidName_ReturnsProblemDetails()
    {
        using var response = await _client.PostAsJsonAsync("/repositories", new { name = "invalid name" });
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetRepositoryReplicas_ReturnsReplicaState()
    {
        var replicas = await _client.GetFromJsonAsync<RepositoryReplicaInfo[]>(
            "/repositories/11111111-1111-1111-1111-111111111111/replicas");

        Assert.NotNull(replicas);
        Assert.Empty(replicas);
    }

    [Fact]
    public async Task ReceivePack_WhenWriteQuorumIsNotReached_ReturnsGitProtocolError()
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentType = new("application/x-git-receive-pack-request");

        using var response = await _client.PostAsync("/quorum-failure.git/git-receive-pack", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-git-receive-pack-result", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1/2", response.Headers.GetValues("X-Git-Write-Quorum").Single());
        Assert.Contains("check replica state or retry the push", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetStorageClusters_ReturnsDatabaseTopology()
    {
        var clusters = await _client.GetFromJsonAsync<StorageClusterInfo[]>("/storage-clusters");

        var cluster = Assert.Single(clusters!);
        Assert.Equal("csharp-cluster", cluster.Name);
        Assert.Single(cluster.Nodes);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ApplyMigrations"] = "false",
                ["Replication:Enabled"] = "false"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRepositoryService>();
            services.RemoveAll<IGitSmartHttpService>();
            services.RemoveAll<IStorageTopologyService>();
            services.AddSingleton<IRepositoryService, FakeRepositoryService>();
            services.AddSingleton<IGitSmartHttpService, FakeGitSmartHttpService>();
            services.AddSingleton<IStorageTopologyService, FakeStorageTopologyService>();
        });
    }
}

internal sealed class FakeRepositoryService : IRepositoryService
{
    private static readonly RepositoryInfo Demo = new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "demo", "cluster", "cluster", "poc/demo.git", true, DateTimeOffset.UnixEpoch);

    public Task<IReadOnlyList<StorageInfo>> GetStoragesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StorageInfo>>([new("cluster", "http://localhost:2305", "cluster")]);
    public Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RepositoryInfo>>([Demo]);
    public Task<RepositoryInfo?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<RepositoryInfo?>(id == Demo.Id ? Demo : null);
    public Task<RepositoryInfo> CreateAsync(string name, CancellationToken cancellationToken) =>
        name.Contains(' ') ? throw new InvalidRepositoryNameException() : Task.FromResult(Demo with { Id = Guid.NewGuid(), Name = name });
    public Task<IReadOnlyList<RepositoryReplicaInfo>> GetReplicasAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RepositoryReplicaInfo>>([]);
}

internal sealed class FakeStorageTopologyService : IStorageTopologyService
{
    private static readonly StorageNodeInfo Node = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "storage-1",
        "http://storage-1:8080",
        "http://storage-1:8080",
        true);

    public Task<IReadOnlyList<StorageClusterInfo>> GetClustersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StorageClusterInfo>>([
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "csharp-cluster", true, [Node])
        ]);

    public Task<StorageClusterInfo> CreateClusterAsync(string name, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<StorageClusterInfo> SetClusterActiveAsync(
        Guid clusterId,
        bool isActive,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<StorageNodeInfo> AddNodeAsync(Guid clusterId, string name, string address,
        string internalAddress, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<StorageNodeInfo> UpdateNodeAsync(Guid clusterId, Guid nodeId, string address,
        string internalAddress, bool isActive, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeGitSmartHttpService : IGitSmartHttpService
{
    public Task WriteInfoRefsAsync(string name, string service, string gitProtocol, Stream responseBody, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReceivePackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken) =>
        name == "quorum-failure"
            ? throw new WriteQuorumNotReachedException(
                Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, 1, 2)
            : Task.CompletedTask;
    public Task UploadPackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken) => Task.CompletedTask;
}
