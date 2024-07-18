using Mono.Cecil;

namespace EnginePrePatcher.Patches;

public class RemoveDiscordConnector : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public bool Patch(AssemblyDefinition assembly)
    {
        var types = assembly.MainModule.Types;
        var removeTypes = types.Where(t => t.Name == "DiscordConnector").ToArray();
        foreach (var t in removeTypes)
        {
            types.Remove(t);
            Console.WriteLine($"Remove DiscordConnector");
        }
        return true;
    }
}
