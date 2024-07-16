using HarmonyLib;

namespace Headless.Patches.SteamConnector;

[HarmonyPatch(typeof(FrooxEngine.SteamConnector), nameof(FrooxEngine.SteamConnector.Initialize))]
public static class DisableSteamAPI
{
    [HarmonyPrefix]
    public static bool Prefix(ref Task<bool> __result)
    {
        __result = Task.FromResult(true);
        return true;
    }
}
