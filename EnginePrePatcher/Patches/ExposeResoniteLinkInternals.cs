using Mono.Cecil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// ResoniteLinkBridge から ResoniteLinkHost を WatsonWsServer なしで使い回すために、
/// private/internal なメンバーを public へ昇格させる。Resonite 側で型構造が変わった場合は
/// このパッチが false を返してビルドが落ちる。
/// </summary>
public class ExposeResoniteLinkInternals : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var hostType = assembly.MainModule.GetType("FrooxEngine.ResoniteLinkHost");
        if (hostType is null)
        {
            Console.WriteLine("FrooxEngine.ResoniteLinkHost type not found");
            return false;
        }

        var messageSender = hostType.Fields.FirstOrDefault(f => f.Name == "_messageSender");
        if (messageSender is null)
        {
            Console.WriteLine("FrooxEngine.ResoniteLinkHost._messageSender field not found");
            return false;
        }
        messageSender.IsPrivate = false;
        messageSender.IsFamily = false;
        messageSender.IsAssembly = false;
        messageSender.IsPublic = true;
        Console.WriteLine("Promoted FrooxEngine.ResoniteLinkHost._messageSender to public");

        var outgoing = hostType.NestedTypes.FirstOrDefault(n => n.Name == "OutgoingMessage");
        if (outgoing is null)
        {
            Console.WriteLine("FrooxEngine.ResoniteLinkHost.OutgoingMessage nested type not found");
            return false;
        }
        outgoing.IsNestedPrivate = false;
        outgoing.IsNestedFamily = false;
        outgoing.IsNestedAssembly = false;
        outgoing.IsNestedPublic = true;
        foreach (var f in outgoing.Fields)
        {
            f.IsPrivate = false;
            f.IsFamily = false;
            f.IsAssembly = false;
            f.IsPublic = true;
        }
        foreach (var m in outgoing.Methods)
        {
            m.IsPrivate = false;
            m.IsFamily = false;
            m.IsAssembly = false;
            m.IsPublic = true;
        }
        Console.WriteLine("Promoted FrooxEngine.ResoniteLinkHost.OutgoingMessage to public");

        var send = hostType.Methods.FirstOrDefault(m =>
            m.Name == "Send" && m.Parameters.Count == 2);
        if (send is null)
        {
            Console.WriteLine("FrooxEngine.ResoniteLinkHost.Send(ClientMetadata, string) method not found");
            return false;
        }
        send.IsPrivate = false;
        send.IsFamily = false;
        send.IsAssembly = false;
        send.IsPublic = true;
        Console.WriteLine("Promoted FrooxEngine.ResoniteLinkHost.Send to public");

        return true;
    }
}
