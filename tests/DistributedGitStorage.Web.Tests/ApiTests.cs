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
                ["Database:ApplyMigrations"] = "false"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRepositoryService>();
            services.RemoveAll<IGitSmartHttpService>();
            services.AddSingleton<IRepositoryService, FakeRepositoryService>();
            services.AddSingleton<IGitSmartHttpService, FakeGitSmartHttpService>();
        });
    }
}

internal sealed class FakeRepositoryService : IRepositoryService
{
    private static readonly RepositoryInfo Demo = new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "demo", "cluster", "cluster", "poc/demo.git", true, DateTimeOffset.UnixEpoch);

    public IReadOnlyList<StorageInfo> GetStorages() => [new("cluster", "http://localhost:2305", "cluster")];
    public Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RepositoryInfo>>([Demo]);
    public Task<RepositoryInfo?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<RepositoryInfo?>(id == Demo.Id ? Demo : null);
    public Task<RepositoryInfo> CreateAsync(string name, CancellationToken cancellationToken) =>
        name.Contains(' ') ? throw new InvalidRepositoryNameException() : Task.FromResult(Demo with { Id = Guid.NewGuid(), Name = name });
}

internal sealed class FakeGitSmartHttpService : IGitSmartHttpService
{
    public Task WriteInfoRefsAsync(string name, string service, string gitProtocol, Stream responseBody, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReceivePackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task UploadPackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken) => Task.CompletedTask;
}
