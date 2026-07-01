using FrooxEngine;
using Google.Protobuf;
using Grpc.Core;
using Headless.Rpc;
using Headless.Libs;
using Headless.Extensions;
using SkyFrost.Base;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override Task<ListSessionsResponse> ListSessions(ListSessionsRequest request, ServerCallContext context)
    {
        var reply = new ListSessionsResponse();
        foreach (var session in _worldService.ListAll())
        {
            reply.Sessions.Add(session.ToProto());
        }
        return Task.FromResult(reply);
    }

    public override Task<GetSessionResponse> GetSession(GetSessionRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        return Task.FromResult(new GetSessionResponse
        {
            Session = session.ToProto()
        });
    }

    public override async Task<StartWorldResponse> StartWorld(StartWorldRequest request, ServerCallContext context)
    {
        if (_isShutdownRequested) throw new RpcException(new Status(StatusCode.Unavailable, "Already shutting down"));

        var reqParam = request.Parameters;
        if (!reqParam.HasLoadWorldUrl && !reqParam.HasLoadWorldPresetName)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require load_world_url or load_world_preset_name!"));
        }
        var session = await _worldService.StartWorldAsync(reqParam.ToResonite());
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed open world!"));
        }
        return new StartWorldResponse
        {
            OpenedSession = session.ToProto()
        };
    }

    public override async Task<StopSessionResponse> StopSession(StopSessionRequest request, ServerCallContext context)
    {
        await _worldService.StopWorldAsync(request.SessionId);
        return new StopSessionResponse();
    }

    public override async Task<SaveSessionWorldResponse> SaveSessionWorld(SaveSessionWorldRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        await _worldService.SaveWorldAsync(session);
        // preset 由来で初回 save された world は CorrespondingRecord が新規生成され
        // World.RecordURL が変わる。WorldSaved event 経由でも届くが、
        // OVERWRITE 直後の controller 応答に間に合うよう同期的にも返す
        return new SaveSessionWorldResponse
        {
            SavedWorldUrl = session.Instance.RecordURL?.ToString() ?? "",
        };
    }

    public override async Task<SaveAsSessionWorldResponse> SaveAsSessionWorld(SaveAsSessionWorldRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        var saved = await _worldService.SaveWorldAsAsync(session, request.Type == SaveAsSessionWorldRequest.Types.SaveAsType.SaveAs);
        if (saved is null)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed save world"));
        }

        return new SaveAsSessionWorldResponse
        {
            SavedRecordUrl = saved.GetUrl(session.Instance.Engine.Cloud.Platform).ToString()
        };
    }

    public override async Task DownloadSessionWorld(
        DownloadSessionWorldRequest request,
        IServerStreamWriter<DownloadSessionWorldResponse> responseStream,
        ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        if (request.Format == WorldBinaryFormat.Unspecified)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require valid format"));
        }
        if (!session.Instance.IsAllowedToSaveWorld())
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "World is not allowed to be saved"));
        }

        var ct = context.CancellationToken;
        // SDK の書き出し API (LZMAHelper.Compress / PackageCreator.BuildPackage) は
        // 出力ストリームに対し Position/SetLength を要求するためシーク可能なバッキングが必須。
        // テンポラリファイルへ書き出してからチャンク送信する。
        var tmpPath = Path.Combine(Path.GetTempPath(), $"headless-export-{Guid.NewGuid():N}.bin");
        try
        {
            try
            {
                await using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.SequentialScan))
                {
                    await session.ExportWorldBinaryAsync(
                        request.Format,
                        fs,
                        request.HasIncludeVariants && request.IncludeVariants,
                        request.HasBrotliQuality ? request.BrotliQuality : null,
                        ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to export world: {ex.Message}"));
            }

            await using var read = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var n = await read.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (n == 0) break;
                await responseStream.WriteAsync(new DownloadSessionWorldResponse
                {
                    Chunk = ByteString.CopyFrom(buffer, 0, n),
                }, ct);
            }
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { /* swallow */ }
        }
    }

    public override async Task<UpdateSessionParametersResponse> UpdateSessionParameters(UpdateSessionParametersRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }

        // World に触らない validation を先に済ませる。RpcException は engine スレッドの外で投げたい。
        SkyFrost.Base.RecordId? correspondingWorldId = null;
        if (request.OverrideCorrespondingWorldId is not null && !string.IsNullOrEmpty(request.OverrideCorrespondingWorldId.Id))
        {
            var id = request.OverrideCorrespondingWorldId;
            correspondingWorldId = new SkyFrost.Base.RecordId(id.OwnerId, id.Id);
            if (!correspondingWorldId.IsValid)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid CorrespondingWorldId"));
            }
        }
        if (request.HasRoleCloudVariable && !CloudVariableHelper.IsValidPath(request.RoleCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid RoleCloudVariable"));
        }
        if (request.HasAllowUserCloudVariable && !CloudVariableHelper.IsValidPath(request.AllowUserCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid AllowUserCloudVariable"));
        }
        if (request.HasDenyUserCloudVariable && !CloudVariableHelper.IsValidPath(request.DenyUserCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid DenyUserCloudVariable"));
        }
        if (request.HasRequiredUserJoinCloudVariable && !CloudVariableHelper.IsValidPath(request.RequiredUserJoinCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid RequiredUserJoinCloudVariable"));
        }

        // RunningSession ローカルのフィールドは sync 不要
        if (request.HasIdleRestartIntervalSeconds)
        {
            session.IdleRestartInterval = request.IdleRestartIntervalSeconds > 0
                ? TimeSpan.FromSeconds(request.IdleRestartIntervalSeconds)
                : TimeSpan.FromSeconds(-1);
        }
        if (request.HasAutoSaveIntervalSeconds)
        {
            session.AutosaveInterval = request.AutoSaveIntervalSeconds > 0
                ? TimeSpan.FromSeconds(request.AutoSaveIntervalSeconds)
                : TimeSpan.FromSeconds(-1);
        }

        // World への書き込みは engine update スレッドにまとめる。
        // Tags は List 代入、Permissions/Cloud variable 系も内部で collection を触るため、
        // gRPC スレッドから直接代入すると engine 側で iter 中の mutation 例外で落ちうる。
        await session.Instance.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);

            if (request.HasName) session.Instance.Name = request.Name;
            if (request.HasDescription) session.Instance.Description = request.Description;
            if (request.HasMaxUsers) session.Instance.MaxUsers = request.MaxUsers;
            if (request.HasAccessLevel) session.Instance.AccessLevel = request.AccessLevel.ToResonite();
            if (request.HasAwayKickMinutes)
            {
                if (request.AwayKickMinutes > 0)
                {
                    session.Instance.AwayKickEnabled = true;
                    session.Instance.AwayKickMinutes = request.AwayKickMinutes;
                }
                else
                {
                    session.Instance.AwayKickEnabled = false;
                    session.Instance.AwayKickMinutes = -1;
                }
            }
            if (request.HasSaveOnExit) session.Instance.SaveOnExit = request.SaveOnExit;
            if (request.HasHideFromPublicListing) session.Instance.HideFromListing = request.HideFromPublicListing;
            if (request.HasAutoSleep) session.Instance.ForceFullUpdateCycle = !request.AutoSleep;
            if (request.HasUseCustomJoinVerifier) session.Instance.UseCustomJoinVerifier = request.UseCustomJoinVerifier;
            if (request.HasMobileFriendly) session.Instance.MobileFriendly = request.MobileFriendly;
            if (correspondingWorldId is not null) session.Instance.CorrespondingWorldId = correspondingWorldId.ToString();
            if (request.HasRoleCloudVariable) session.Instance.Permissions.DefaultRoleCloudVariable = request.RoleCloudVariable;
            if (request.HasAllowUserCloudVariable) session.Instance.AllowUserCloudVariable = request.AllowUserCloudVariable;
            if (request.HasDenyUserCloudVariable) session.Instance.DenyUserCloudVariable = request.DenyUserCloudVariable;
            if (request.HasRequiredUserJoinCloudVariable) session.Instance.RequiredUserJoinCloudVariable = request.RequiredUserJoinCloudVariable;
            if (request.HasRequiredUserJoinCloudVariableDenyMessage) session.Instance.RequiredUserJoinCloudVariableDenyMessage = request.RequiredUserJoinCloudVariableDenyMessage;
            if (request.UpdateTags) session.Instance.Tags = request.Tags;
        });

        // SaveOnExit / IdleRestartInterval / ForceFullUpdateCycle / Tags(List 代入) などは
        // Sync の OnValueChange を踏まないため、明示的にスナップショット送信をキックする。
        session.NotifyParametersChanged();

        return new UpdateSessionParametersResponse();
    }

    public override async Task<SpawnItemResponse> SpawnItem(SpawnItemRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid item URL: {request.Url}"));
        }
        // file:// / javascript: / data: 等の想定外 scheme を弾く。
        if (uri.Scheme != "https" && uri.Scheme != "resrec")
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"URL scheme must be https or resrec: {uri.Scheme}"));
        }

        // ロード完了を待たずに応答するため fire-and-forget。
        // world への書き込み (RootSlot.AddSlot / LoadObjectAsync) は engine スレッドで行う必要があるので
        // ToWorld で切り替えてから実行する (UpdateSessionParameters と同じ pattern)。
        _ = session.Instance.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);
            var slot = session.Instance.RootSlot.AddSlot("Headless Spawn");
            try
            {
                var ok = await slot.LoadObjectAsync(uri);
                if (!ok)
                {
                    _logger.LogWarning("SpawnItem: LoadObjectAsync returned false for {Url}", uri);
                    slot.Destroy();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SpawnItem: exception loading {Url}", uri);
                try { slot.Destroy(); } catch { }
            }
        });

        return new SpawnItemResponse();
    }
}
