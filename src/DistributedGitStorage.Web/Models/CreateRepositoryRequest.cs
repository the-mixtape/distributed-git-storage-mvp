using System.ComponentModel.DataAnnotations;

namespace DistributedGitStorage.Web.Models;

public sealed class CreateRepositoryRequest
{
    [Required]
    public required string Name { get; init; }
}
