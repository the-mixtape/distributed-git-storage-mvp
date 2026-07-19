namespace GitalyControlPlane.Services.Options;

public sealed class GitalyOptions
{
    public string SidechannelGatewayAddress { get; init; } = "http://localhost:8090";
    public List<GitalyStorageOptions> Storages { get; init; } = [];
}

public sealed class GitalyStorageOptions
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string SidechannelAddress { get; init; }
    public string StorageName { get; init; } = "default";
}
