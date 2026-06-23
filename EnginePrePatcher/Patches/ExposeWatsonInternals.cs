using Mono.Cecil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// ResoniteLinkBridge から ResoniteLinkService の OnMessage/OnClose を gRPC bidi stream の
/// 内容から直接呼ぶために、WatsonWebsocket のイベント引数の internal ctor を public へ昇格する。
/// シグネチャが変わった場合はこのパッチが false を返してビルドが落ちる。
/// </summary>
public class ExposeWatsonInternals : IAssemblyPatch
{
    public string TargetAssemblyPath => "WatsonWebsocket.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        if (!PromoteCtor(assembly, "WatsonWebsocket.MessageReceivedEventArgs", 3))
        {
            return false;
        }
        if (!PromoteCtor(assembly, "WatsonWebsocket.DisconnectionEventArgs", 1))
        {
            return false;
        }
        return true;
    }

    private static bool PromoteCtor(AssemblyDefinition assembly, string typeFullName, int parameterCount)
    {
        var type = assembly.MainModule.GetType(typeFullName);
        if (type is null)
        {
            Console.WriteLine($"{typeFullName} type not found");
            return false;
        }

        var ctor = type.Methods.FirstOrDefault(m =>
            m.IsConstructor && !m.IsStatic && m.Parameters.Count == parameterCount);
        if (ctor is null)
        {
            Console.WriteLine($"{typeFullName} {parameterCount}-arg ctor not found");
            return false;
        }
        ctor.IsPrivate = false;
        ctor.IsFamily = false;
        ctor.IsAssembly = false;
        ctor.IsPublic = true;
        Console.WriteLine($"Promoted {typeFullName} {parameterCount}-arg ctor to public");
        return true;
    }
}
