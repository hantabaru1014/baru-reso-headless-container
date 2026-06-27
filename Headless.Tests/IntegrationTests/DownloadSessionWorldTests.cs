using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for the DownloadSessionWorld server-streaming RPC. The
/// happy path exercises the engine snapshot → DataTreeConverter → chunked
/// response pipeline, which is the most likely place to break when the
/// Resonite SDK changes World.SaveWorld / DataTreeConverter signatures.
/// </summary>
[Collection("Container")]
public class DownloadSessionWorldTests
{
    private readonly ContainerFixture _fixture;

    public DownloadSessionWorldTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DownloadSessionWorld_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        using var call = client.DownloadSessionWorld(new DownloadSessionWorldRequest
        {
            SessionId = "S-U-does-not-exist:nope",
            Format = WorldBinaryFormat._7Zbson,
        });

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync())
            {
            }
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task DownloadSessionWorld_UnspecifiedFormat_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            using var call = client.DownloadSessionWorld(new DownloadSessionWorldRequest
            {
                SessionId = sessionId,
                Format = WorldBinaryFormat.Unspecified,
            });

            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await foreach (var _ in call.ResponseStream.ReadAllAsync())
                {
                }
            });
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
    }

    [Fact]
    public async Task DownloadSessionWorld_SevenZbsonHappyPath_StreamsNonEmptyPayload()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            using var call = client.DownloadSessionWorld(new DownloadSessionWorldRequest
            {
                SessionId = sessionId,
                Format = WorldBinaryFormat._7Zbson,
            });

            long total = 0;
            int chunks = 0;
            await foreach (var part in call.ResponseStream.ReadAllAsync())
            {
                chunks++;
                total += part.Chunk.Length;
                // Guard against a runaway test if the SDK starts streaming
                // megabytes of data unexpectedly.
                if (total > 64L * 1024 * 1024)
                {
                    break;
                }
            }

            Assert.True(chunks > 0, "Expected at least one chunk in the download stream");
            Assert.True(total > 0, "Expected the download stream to contain a non-empty payload");
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
    }
}
