using GitalyControlPlane.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GitalyControlPlane.Web.Controllers;

[ApiController]
[Route("storages")]
public sealed class StoragesController(IRepositoryService repositoryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StorageInfo>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<StorageInfo>> Get() => Ok(repositoryService.GetStorages());
}
