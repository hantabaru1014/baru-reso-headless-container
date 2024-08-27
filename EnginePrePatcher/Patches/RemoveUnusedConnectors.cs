using Mono.Cecil;

namespace EnginePrePatcher.Patches;

public class RemoveUnusedConnectors : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public bool Patch(AssemblyDefinition assembly)
    {
        var types = assembly.MainModule.Types;
        var removeTypes = types.Where(t =>
        {
            switch (t.Name)
            {
                case "SteamConnector":
                case "SteamVoiceAudioInputDriver":
                case "SteamListener":
                case "SteamConnection":
                case "SteamNetworkManager":
                case "DiscordConnector":
                    return true;
            }
            return false;
        }).ToArray();
        foreach (var t in removeTypes)
        {
            types.Remove(t);
            Console.WriteLine($"Remove Type : {t.FullName}");
        }
        return true;
    }
}
