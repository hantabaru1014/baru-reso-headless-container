using System.Net;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Headless.Tests.Fixtures;

public class ContainerFixture : IAsyncLifetime
{
    private const string DefaultImageName = "ghcr.io/hantabaru1014/baru-reso-headless-container";
    private const string ContainerNamePrefix = "headless-test-";
    private const int GrpcPort = 5000;

    private readonly DockerClient _dockerClient;
    private string? _containerId;
    private readonly string _containerName;

    public string GrpcEndpoint => $"http://localhost:{HostPort}";
    public int HostPort { get; private set; }
    public string ContainerId => _containerId ?? throw new InvalidOperationException("Container not started");

    public ContainerFixture()
    {
        _dockerClient = new DockerClientConfiguration().CreateClient();
        _containerName = $"{ContainerNamePrefix}{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        // Cleanup any existing test containers
        await CleanupExistingContainersAsync();

        // Get available port
        HostPort = GetAvailablePort();

        // Create and start container
        var response = await _dockerClient.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = GetImageTag(),
                Name = _containerName,
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{GrpcPort}/tcp"] = new List<PortBinding>
                        {
                            new() { HostPort = HostPort.ToString() }
                        }
                    }
                },
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{GrpcPort}/tcp"] = default
                },
                // Run in guest mode (no authentication)
                Env = new List<string>
                {
                    "RpcHostUrl=http://+:5000"
                }
            });

        _containerId = response.ID;
        await _dockerClient.Containers.StartContainerAsync(_containerId, null);
    }

    public async Task DisposeAsync()
    {
        if (_containerId != null)
        {
            try
            {
                await _dockerClient.Containers.StopContainerAsync(_containerId,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 10 });
            }
            catch
            {
                // Container may already be stopped
            }

            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(_containerId,
                    new ContainerRemoveParameters { Force = true });
            }
            catch
            {
                // Container may already be removed
            }
        }
        _dockerClient.Dispose();
    }

    public async Task<string> GetLogsAsync()
    {
        if (_containerId == null) return string.Empty;

        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = false
        };

        using var multiplexedStream = await _dockerClient.Containers.GetContainerLogsAsync(
            _containerId,
            false,
            parameters);

        var result = new StringBuilder();
        var buffer = new byte[81920];

        while (true)
        {
            var readResult = await multiplexedStream.ReadOutputAsync(buffer, 0, buffer.Length, CancellationToken.None);
            if (readResult.EOF)
                break;

            var text = Encoding.UTF8.GetString(buffer, 0, readResult.Count);
            result.Append(text);
        }

        return result.ToString();
    }

    private static string GetImageTag()
    {
        // Get image tag from environment variable, default to "test" tag
        return Environment.GetEnvironmentVariable("TEST_IMAGE_TAG")
            ?? $"{DefaultImageName}:test";
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task CleanupExistingContainersAsync()
    {
        var containers = await _dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool>
                    {
                        [ContainerNamePrefix] = true
                    }
                }
            });

        foreach (var container in containers)
        {
            try
            {
                await _dockerClient.Containers.StopContainerAsync(container.ID,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 5 });
            }
            catch
            {
                // Ignore errors
            }

            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true });
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}
