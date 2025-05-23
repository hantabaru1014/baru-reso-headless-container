using Mono.Cecil;

namespace EnginePrePatcher.Patches;

public class RemoveUnusedConnectors : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => new List<string> { "lib-client-csharp.dll", "Google.Protobuf.dll" };

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
                // こいつらが依存しているGoogle.Protobuf.dllが謎に超古い&使っているGrpcライブラリと競合して邪魔
                case "OmniceptTrackingDriver":
                case "GliaHandler":
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
