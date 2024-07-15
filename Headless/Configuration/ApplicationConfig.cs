namespace Headless.Configuration;

public record ApplicationConfig
(
    string RpcHostUrl,
    string DataDirectoryPath,
    string? HeadlessUserCredential = null,
    string? HeadlessUserPassword = null,
    int? BackgroundWorkers = null,
    int? PriorityWorkers = null
)
{
    public ApplicationConfig() : this("http://0.0.0.0:5000", Directory.GetCurrentDirectory())
    {
    }
}
