using FrooxEngine;
using FrooxEngine.ProtoFlux;
using Grpc.Core;
using Headless.Rpc;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override async Task<SendDynamicImpulseResponse> SendDynamicImpulse(SendDynamicImpulseRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        if (string.IsNullOrEmpty(request.Tag))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require tag"));
        }

        var handler = ProtoFluxHelper.DynamicImpulseHandler;
        if (handler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "DynamicImpulseHandler is not registered"));
        }

        var tag = request.Tag;
        // ProtoFlux Trigger 系は World の Refresh コルーチン上で発火させる必要があるため、
        // 既存 UpdateSessionParameters と同様に ToWorld でスレッドを合わせる。
        var receivers = await session.Instance.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);
            var root = session.Instance.RootSlot;
            return request.ValueCase switch
            {
                SendDynamicImpulseRequest.ValueOneofCase.StringValue =>
                    await handler.TriggerAsyncDynamicImpulseWithArgument(root, tag, excludeDisabled: false, request.StringValue),
                SendDynamicImpulseRequest.ValueOneofCase.IntValue =>
                    await handler.TriggerAsyncDynamicImpulseWithArgument(root, tag, excludeDisabled: false, request.IntValue),
                SendDynamicImpulseRequest.ValueOneofCase.FloatValue =>
                    await handler.TriggerAsyncDynamicImpulseWithArgument(root, tag, excludeDisabled: false, request.FloatValue),
                _ => await handler.TriggerAsyncDynamicImpulse(root, tag, excludeDisabled: false),
            };
        });

        return new SendDynamicImpulseResponse
        {
            TriggeredReceivers = receivers,
        };
    }

    public override Task<RunGarbageCollectionResponse> RunGarbageCollection(RunGarbageCollectionRequest request, ServerCallContext context)
    {
        // 公式 headless の `gc` コマンドと同一挙動: 純粋な System.GC.Collect() のみ。
        // Engine 側の追加処理はない (Engine には手動 GC トリガ用の公開 API 自体が存在しない)。
        GC.Collect();
        return Task.FromResult(new RunGarbageCollectionResponse());
    }

    public override Task<GetWorldDebugStateResponse> GetWorldDebugState(GetWorldDebugStateRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }

        // 公式 debugWorldState は世界の全リストに対して UniLog.Log で以下を出す。
        // ここでは指定セッション1件のみ返す形にする。
        //   RawName / Stage / SyncTick / SessionState / StopProcessingFlag /
        //   MessagesToProcess / TotalProcessedMessages / MessagesToTransmit /
        //   CurrentlyProcessingSyncMessage / CurrentlyDecodingStream
        var world = session.Instance;
        var response = new GetWorldDebugStateResponse
        {
            WorldName = world.RawName ?? "",
            LocalWorldHandle = world.LocalWorldHandle,
            Stage = world.Stage.ToString(),
            SyncTick = world.SyncTick,
            StateVersion = world.StateVersion,
            SlotCount = world.SlotCount,
            UserCount = world.UserCount,
            ActiveUserCount = world.ActiveUserCount,
        };

        var innerSession = world.Session;
        if (innerSession is not null)
        {
            var syncMgr = innerSession.Sync;
            response.SessionSyncLoopStage = syncMgr.SyncLoopStage.ToString();
            response.SessionStopProcessingFlag = syncMgr.StopProcessingFlag;
            response.SessionMessagesToProcessCount = syncMgr.MessagesToProcessCount;
            response.SessionTotalProcessedMessages = syncMgr.TotalProcessedMessages;
            response.SessionMessagesToTransmitCount = innerSession.Messages.Outgoing.MessagesToTransmitCount;

            var current = syncMgr.CurrentlyProcessingSyncMessage;
            if (current is not null)
            {
                response.CurrentlyProcessingSyncMessage = current.ToString();
            }
        }

        var decoding = world.SyncController?.CurrentlyDecodingStream;
        if (decoding is not null)
        {
            response.CurrentlyDecodingStream = decoding.ToString();
        }

        return Task.FromResult(response);
    }
}
