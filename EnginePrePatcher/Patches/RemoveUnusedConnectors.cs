using Mono.Cecil;

namespace EnginePrePatcher.Patches;

public class RemoveUnusedConnectors : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => new List<string> { "Google.Protobuf.dll" };

    public bool Patch(AssemblyDefinition assembly)
    {
        var types = assembly.MainModule.Types;
        var removeTypes = types.Where(t =>
        {
            switch (t.Name)
            {
                // 依存しているネイティブライブラリが邪魔
                case "SteamConnector":
                case "SteamVoiceAudioInputDriver":
                case "SteamListener":
                case "SteamConnection":
                case "SteamNetworkManager":
                // 依存しているネイティブライブラリが邪魔
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
