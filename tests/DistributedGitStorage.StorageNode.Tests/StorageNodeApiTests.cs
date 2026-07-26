using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DistributedGitStorage.StorageNode.Tests;

public sealed class StorageNodeApiTests : IDisposable
{
    private readonly StorageNodeFactory _factory = new();
    private readonly HttpClient _client;

    public StorageNodeApiTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task CreateRepository_CreatesBareRepository()
    {
        var repositoryId = Guid.NewGuid();

        using var createResponse = await _client.PostAsJsonAsync(
            $"/internal/repositories/{repositoryId}",
            new { defaultBranch = "main" });
        using var existsResponse = await _client.GetAsync(
            $"/internal/repositories/{repositoryId}");

        Assert.Equal(HttpStatusCode.NoContent, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, existsResponse.StatusCode);
        var head = await File.ReadAllTextAsync(
            Path.Combine(_factory.RootPath, $"{repositoryId:N}.git", "HEAD"));
        Assert.Equal("ref: refs/heads/main", head.Trim());
    }

    [Fact]
    public async Task InfoRefs_ReturnsGitSmartHttpAdvertisement()
    {
        var repositoryId = Guid.NewGuid();
        using var createResponse = await _client.PostAsJsonAsync(
            $"/internal/repositories/{repositoryId}",
            new { defaultBranch = "main" });
        createResponse.EnsureSuccessStatusCode();

        var bytes = await _client.GetByteArrayAsync(
            $"/repositories/{repositoryId}.git/info/refs?service=git-upload-pack");
        var prefix = Encoding.ASCII.GetString(bytes.AsSpan(0, 34));

        Assert.Equal("001e# service=git-upload-pack\n0000", prefix);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_factory.RootPath))
        {
            Directory.Delete(_factory.RootPath, recursive: true);
        }
    }
}

public sealed class StorageNodeFactory : WebApplicationFactory<Program>
{
    public string RootPath { get; } = Path.Combine(
        Path.GetTempPath(),
        "distributed-git-storage-tests",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = RootPath
            }));
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}
