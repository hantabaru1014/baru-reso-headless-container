using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// SessionSyncManager の lastProcessingStopped は初期状態が set のため、ワールドの最初のフレームの ResumeProcessing は
/// メッセージ処理スレッドが StopProcessing の通知 (stopProcessing = true) を実際に観測したかを待たずに stopProcessing を false に戻す。
/// その間にメッセージ処理スレッドがスケジュールされないと、スレッドは停止要求を見逃してメッセージ処理の待機に戻り、
/// 最初のフレームの StopProcessing が取った processingLoopLock を解放しなくなる。次のフレームの StopProcessing は
/// ロックを 15 秒待って "Failed to take processing loop lock" を投げ、ワールドが起動直後に落ちる。
/// ホストの場合、StartSyncLoop の時点ではワールドがまだ WorldManager に登録されておらず StopProcessing は一度も呼ばれていないので、
/// メッセージ処理スレッドを開始する前に lastProcessingStopped を reset し、最初のフレームも以降のフレームと同様に停止を待たせる。
/// </summary>
public class FixFirstSyncCycleRace : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var syncManagerType = assembly.MainModule.GetType("FrooxEngine.SessionSyncManager");
        if (syncManagerType is null)
        {
            Console.WriteLine("FrooxEngine.SessionSyncManager type not found");
            return false;
        }

        var lastProcessingStopped = syncManagerType.Fields.FirstOrDefault(f => f.Name == "lastProcessingStopped");
        var startSyncLoop = syncManagerType.Methods.FirstOrDefault(m =>
            m.Name == "StartSyncLoop" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.MetadataType == MetadataType.Boolean);
        if (lastProcessingStopped is null || startSyncLoop is null)
        {
            Console.WriteLine("SessionSyncManager members (lastProcessingStopped/StartSyncLoop(bool)) not found");
            return false;
        }

        var reset = lastProcessingStopped.FieldType.Resolve()?.Methods
            .FirstOrDefault(m => m.Name == "Reset" && m.Parameters.Count == 0);
        if (reset is null)
        {
            Console.WriteLine($"{lastProcessingStopped.FieldType.FullName}.Reset() not found");
            return false;
        }

        // if (isHost) this.lastProcessingStopped.Reset();
        var il = startSyncLoop.Body.GetILProcessor();
        var originalFirst = startSyncLoop.Body.Instructions[0];
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Brfalse, originalFirst));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldfld, lastProcessingStopped));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Callvirt, assembly.MainModule.ImportReference(reset)));

        Console.WriteLine("Patched FrooxEngine.SessionSyncManager.StartSyncLoop to reset lastProcessingStopped for host sessions");
        return true;
    }
}
