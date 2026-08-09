using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DistributedGitStorage.StorageNode.Models;

namespace DistributedGitStorage.StorageNode.Services;

public sealed class GitRepositoryStore
{
    private readonly string _rootPath;
    private readonly ILogger<GitRepositoryStore> _logger;

    public GitRepositoryStore(IConfiguration configuration, IWebHostEnvironment environment, ILogger<GitRepositoryStore> logger)
    {
        var configuredPath = configuration["Storage:RootPath"] ?? "data/repositories";
        _rootPath = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        _logger = logger;
        Directory.CreateDirectory(_rootPath);
    }

    public bool Exists(Guid repositoryId) => Directory.Exists(GetRepositoryPath(repositoryId));

    public async Task<RepositoryStateResponse> GetStateAsync(
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        var repositoryPath = GetRepositoryPath(repositoryId);
        if (!Directory.Exists(repositoryPath))
        {
            return new RepositoryStateResponse(false, null, null);
        }

        var refs = await RunGitCaptureAsync(
            ["-C", repositoryPath, "for-each-ref", "--format=%(refname)%00%(objectname)"],
            cancellationToken);
        var normalizedRefs = string.Join('\n', refs
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal));
        var refsHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRefs)));
        var head = (await RunGitCaptureAsync(
            ["-C", repositoryPath, "symbolic-ref", "HEAD"],
            cancellationToken)).Trim();
        return new RepositoryStateResponse(true, refsHash, head);
    }

    public async Task CreateAsync(Guid repositoryId, string defaultBranch, CancellationToken cancellationToken)
    {
        var repositoryPath = GetRepositoryPath(repositoryId);
        if (Directory.Exists(repositoryPath))
        {
            return;
        }

        Directory.CreateDirectory(repositoryPath);
        try
        {
            await RunGitAsync(
                ["init", "--bare", $"--initial-branch={defaultBranch}", repositoryPath],
                cancellationToken);
        }
        catch
        {
            Directory.Delete(repositoryPath, recursive: true);
            throw;
        }
    }

    public Task DeleteAsync(Guid repositoryId)
    {
        var repositoryPath = GetRepositoryPath(repositoryId);
        if (Directory.Exists(repositoryPath))
        {
            Directory.Delete(repositoryPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task ReplicateAsync(Guid repositoryId, string sourceUrl, CancellationToken cancellationToken)
    {
        var repositoryPath = GetRequiredRepositoryPath(repositoryId);
        await RunGitAsync(
            [
                "-C", repositoryPath,
                "fetch", "--prune", "--force", sourceUrl,
                "+refs/*:refs/*"
            ],
            cancellationToken);
    }

    public async Task AdvertiseRefsAsync(
        Guid repositoryId,
        string service,
        string gitProtocol,
        Stream response,
        CancellationToken cancellationToken)
    {
        var repositoryPath = GetRequiredRepositoryPath(repositoryId);
        var serviceCommand = GetServiceCommand(service);
        var announcement = Encoding.ASCII.GetBytes($"# service={service}\n");
        var prefix = Encoding.ASCII.GetBytes($"{announcement.Length + 4:x4}");
        await response.WriteAsync(prefix, cancellationToken);
        await response.WriteAsync(announcement, cancellationToken);
        await response.WriteAsync("0000"u8.ToArray(), cancellationToken);

        await RunStreamingGitAsync(
            [serviceCommand, "--stateless-rpc", "--advertise-refs", repositoryPath],
            gitProtocol,
            Stream.Null,
            response,
            cancellationToken);
    }

    public Task ExecuteRpcAsync(
        Guid repositoryId,
        string service,
        string gitProtocol,
        Stream request,
        Stream response,
        CancellationToken cancellationToken)
    {
        var repositoryPath = GetRequiredRepositoryPath(repositoryId);
        return RunStreamingGitAsync(
            [GetServiceCommand(service), "--stateless-rpc", repositoryPath],
            gitProtocol,
            request,
            response,
            cancellationToken);
    }

    private string GetRepositoryPath(Guid repositoryId) =>
        Path.Combine(_rootPath, $"{repositoryId:N}.git");

    private string GetRequiredRepositoryPath(Guid repositoryId)
    {
        var repositoryPath = GetRepositoryPath(repositoryId);
        return Directory.Exists(repositoryPath)
            ? repositoryPath
            : throw new DirectoryNotFoundException($"Repository '{repositoryId}' was not found on this node.");
    }

    private static string GetServiceCommand(string service) => service switch
    {
        "git-upload-pack" => "upload-pack",
        "git-receive-pack" => "receive-pack",
        _ => throw new ArgumentException($"Unsupported Git service '{service}'.", nameof(service))
    };

    private async Task RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        _ = await RunGitCaptureAsync(arguments, cancellationToken);
    }

    private async Task<string> RunGitCaptureAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = StartGit(arguments, gitProtocol: null);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            await StopProcessAsync(process);
            await ObserveFailureAsync(outputTask);
            await ObserveFailureAsync(errorTask);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        _logger.LogDebug("Git command completed: {Output}", output.Trim());
        return output;
    }

    private static async Task RunStreamingGitAsync(
        IReadOnlyList<string> arguments,
        string gitProtocol,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        using var process = StartGit(arguments, gitProtocol);
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        try
        {
            if (input != Stream.Null)
            {
                await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
            }

            process.StandardInput.Close();
            await Task.WhenAll(outputTask, process.WaitForExitAsync(cancellationToken));
        }
        catch
        {
            await StopProcessAsync(process);
            await ObserveFailureAsync(outputTask);
            await ObserveFailureAsync(errorTask);
            throw;
        }
        finally
        {
            process.StandardInput.Close();
        }

        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git RPC failed: {error}");
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            process.StandardInput.Close();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            // The process exited between the state check and the kill request.
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The process is already unavailable; there is nothing left to wait for.
        }
    }

    private static async Task ObserveFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Preserve the original exception that initiated process termination.
        }
    }

    private static Process StartGit(IReadOnlyList<string> arguments, string? gitProtocol)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(gitProtocol))
        {
            startInfo.Environment["GIT_PROTOCOL"] = gitProtocol;
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the git process.");
    }
}
