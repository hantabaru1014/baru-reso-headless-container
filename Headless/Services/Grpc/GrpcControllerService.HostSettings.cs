using FrooxEngine;
using Grpc.Core;
using Headless.Extensions;
using Headless.Rpc;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override async Task<GetHostSettingsResponse> GetHostSettings(GetHostSettingsRequest request, ServerCallContext context)
    {
        var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
        var allowedList = securitySettings.Entries.Select(entry =>
        {
            var types = new List<AllowedAccessEntry.Types.AccessType>();
            if (entry.Value.AllowHTTP_Requests)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.Http);
            }
            if (entry.Value.AllowWebsockets)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.Websocket);
            }
            if (entry.Value.AllowOSC_Receiving)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.OscReceiving);
            }
            if (entry.Value.AllowOSC_Sending)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.OscSending);
            }
            return new AllowedAccessEntry
            {
                Host = entry.Key,
                Ports = { entry.Value.AllowedPorts },
                AccessTypes = { types },
            };
        });
        var response = new GetHostSettingsResponse
        {
            TickRate = _runnerService.TickRate,
            MaxConcurrentAssetTransfers = SessionAssetTransferer.OverrideMaxConcurrentTransfers ?? 4,
            AllowedUrlHosts = { allowedList },
            AutoSpawnItems = { _worldService.AutoSpawnItems.Select(uri => uri.ToString()) },
        };
        if (_engine.Cloud.UniverseID is not null)
        {
            response.UniverseId = _engine.Cloud.UniverseID;
        }
        if (_engine.UsernameOverride is not null)
        {
            response.UsernameOverride = _engine.UsernameOverride;
        }

        return response;
    }

    public override async Task<GetStartupConfigToRestoreResponse> GetStartupConfigToRestore(GetStartupConfigToRestoreRequest request, ServerCallContext context)
    {
        var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
        var allowedList = securitySettings.Entries.Select(entry =>
        {
            var types = new List<AllowedAccessEntry.Types.AccessType>();
            if (entry.Value.AllowHTTP_Requests)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.Http);
            }
            if (entry.Value.AllowWebsockets)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.Websocket);
            }
            if (entry.Value.AllowOSC_Receiving)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.OscReceiving);
            }
            if (entry.Value.AllowOSC_Sending)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.OscSending);
            }
            return new AllowedAccessEntry
            {
                Host = entry.Key,
                Ports = { entry.Value.AllowedPorts },
                AccessTypes = { types },
            };
        });
        var config = new StartupConfig
        {
            TickRate = _runnerService.TickRate,
            MaxConcurrentAssetTransfers = SessionAssetTransferer.OverrideMaxConcurrentTransfers ?? 4,
            AllowedUrlHosts = { allowedList },
            AutoSpawnItems = { _worldService.AutoSpawnItems.Select(uri => uri.ToString()) },
        };
        if (_engine.Cloud.UniverseID is not null)
        {
            config.UniverseId = _engine.Cloud.UniverseID;
        }
        if (_engine.UsernameOverride is not null)
        {
            config.UsernameOverride = _engine.UsernameOverride;
        }

        if (request.IncludeStartWorlds)
        {
            foreach (var session in _worldService.ListAll())
            {
                config.StartWorlds.Add(session.GenerateStartupParameters().ToProto());
            }
        }

        return new GetStartupConfigToRestoreResponse
        {
            StartupConfig = config
        };
    }

    public override Task<UpdateHostSettingsResponse> UpdateHostSettings(UpdateHostSettingsRequest request, ServerCallContext context)
    {
        if (request.HasTickRate && request.TickRate > 0)
        {
            _runnerService.TickRate = request.TickRate;
        }
        if (request.HasMaxConcurrentAssetTransfers && request.MaxConcurrentAssetTransfers > 0)
        {
            SessionAssetTransferer.OverrideMaxConcurrentTransfers = request.MaxConcurrentAssetTransfers;
        }
        if (request.HasUsernameOverride && request.UsernameOverride.Length > 0)
        {
            _engine.UsernameOverride = request.UsernameOverride;
        }
        if (request.UpdateAutoSpawnItems)
        {
            _worldService.AutoSpawnItems = request.AutoSpawnItems.Select(uri =>
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
                {
                    return result;
                }
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid item URL: {uri}"));
            }).ToList();
        }

        return Task.FromResult(new UpdateHostSettingsResponse());
    }

    public override Task<AllowHostAccessResponse> AllowHostAccess(AllowHostAccessRequest request, ServerCallContext context)
    {
        Userspace.UserspaceWorld.RunSynchronously(async () =>
        {
            var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
            switch (request.AccessType)
            {
                case AllowedAccessEntry.Types.AccessType.Http:
                    securitySettings.AllowHTTP_Requests(request.Host, request.Port);
                    break;
                case AllowedAccessEntry.Types.AccessType.Websocket:
                    securitySettings.AllowWebsocket(request.Host, request.Port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscReceiving:
                    securitySettings.AllowOSC_Receiving(request.Port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscSending:
                    securitySettings.AllowOSC_Sending(request.Host, request.Port);
                    break;
            }
        });

        return Task.FromResult(new AllowHostAccessResponse());
    }

    public override Task<DenyHostAccessResponse> DenyHostAccess(DenyHostAccessRequest request, ServerCallContext context)
    {
        Userspace.UserspaceWorld.RunSynchronously(async () =>
        {
            var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
            int? port = null;
            if (request.HasPort && request.Port > 0)
            {
                port = request.Port;
            }
            switch (request.AccessType)
            {
                case AllowedAccessEntry.Types.AccessType.Http:
                    securitySettings.BlockHTTP_Requests(request.Host, port);
                    break;
                case AllowedAccessEntry.Types.AccessType.Websocket:
                    securitySettings.BlockWebsocket(request.Host, port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscReceiving:
                    securitySettings.BlockOSC_Receiving(port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscSending:
                    securitySettings.BlockOSC_Sending(request.Host, port);
                    break;
            }
        });

        return Task.FromResult(new DenyHostAccessResponse());
    }
}
