using System.ComponentModel.DataAnnotations;

namespace GitalyControlPlane.Web.Models;

public sealed class CreateRepositoryRequest
{
    [Required]
    public required string Name { get; init; }
}
