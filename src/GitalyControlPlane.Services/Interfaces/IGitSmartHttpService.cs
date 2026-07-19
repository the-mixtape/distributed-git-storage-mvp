namespace GitalyControlPlane.Services.Interfaces;

public interface IGitSmartHttpService
{
    Task WriteInfoRefsAsync(string name, string service, string gitProtocol, Stream responseBody, CancellationToken cancellationToken);
    Task ReceivePackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken);
    Task UploadPackAsync(string name, string gitProtocol, Stream requestBody, Stream responseBody, CancellationToken cancellationToken);
}
