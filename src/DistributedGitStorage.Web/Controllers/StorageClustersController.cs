using DistributedGitStorage.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGitStorage.Web.Controllers;

[ApiController]
[Route("storage-clusters")]
public sealed class StorageClustersController(IStorageTopologyService topologyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StorageClusterInfo>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await topologyService.GetClustersAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<StorageClusterInfo>> Create(
        CreateStorageClusterRequest request,
        CancellationToken cancellationToken)
    {
        var cluster = await topologyService.CreateClusterAsync(request.Name, cancellationToken);
        return Created($"/storage-clusters/{cluster.Id}", cluster);
    }

    [HttpPut("{clusterId:guid}")]
    public async Task<ActionResult<StorageClusterInfo>> SetActive(
        Guid clusterId,
        UpdateStorageClusterRequest request,
        CancellationToken cancellationToken) =>
        Ok(await topologyService.SetClusterActiveAsync(clusterId, request.IsActive, cancellationToken));

    [HttpPost("{clusterId:guid}/nodes")]
    public async Task<ActionResult<StorageNodeInfo>> AddNode(
        Guid clusterId,
        CreateStorageNodeRequest request,
        CancellationToken cancellationToken)
    {
        var node = await topologyService.AddNodeAsync(
            clusterId,
            request.Name,
            request.Address,
            request.InternalAddress,
            cancellationToken);
        return Created($"/storage-clusters/{clusterId}/nodes/{node.Id}", node);
    }

    [HttpPut("{clusterId:guid}/nodes/{nodeId:guid}")]
    public async Task<ActionResult<StorageNodeInfo>> UpdateNode(
        Guid clusterId,
        Guid nodeId,
        UpdateStorageNodeRequest request,
        CancellationToken cancellationToken) =>
        Ok(await topologyService.UpdateNodeAsync(
            clusterId,
            nodeId,
            request.Address,
            request.InternalAddress,
            request.IsActive,
            cancellationToken));
}

public sealed record CreateStorageClusterRequest(string Name);
public sealed record UpdateStorageClusterRequest(bool IsActive);
public sealed record CreateStorageNodeRequest(string Name, string Address, string InternalAddress);
public sealed record UpdateStorageNodeRequest(string Address, string InternalAddress, bool IsActive);
