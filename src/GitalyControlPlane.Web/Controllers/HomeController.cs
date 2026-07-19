using Microsoft.AspNetCore.Mvc;

namespace GitalyControlPlane.Web.Controllers;

[ApiController]
public sealed class HomeController : ControllerBase
{
    [HttpGet("/")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Index() => Redirect("/swagger");
}
