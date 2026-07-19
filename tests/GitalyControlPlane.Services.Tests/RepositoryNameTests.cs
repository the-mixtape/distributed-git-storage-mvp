using GitalyControlPlane.Services.Exceptions;
using GitalyControlPlane.Services.Services;

namespace GitalyControlPlane.Services.Tests;

public sealed class RepositoryNameTests
{
    [Theory]
    [InlineData(" Demo ", "demo")]
    [InlineData("team.api_v2", "team.api_v2")]
    [InlineData("repo-42", "repo-42")]
    public void NormalizeName_ReturnsCanonicalName(string source, string expected) =>
        Assert.Equal(expected, GitalyRepositoryService.NormalizeName(source));

    [Theory]
    [InlineData("")]
    [InlineData("invalid name")]
    [InlineData("/root")]
    public void NormalizeName_RejectsInvalidName(string source) =>
        Assert.Throws<InvalidRepositoryNameException>(() => GitalyRepositoryService.NormalizeName(source));
}
