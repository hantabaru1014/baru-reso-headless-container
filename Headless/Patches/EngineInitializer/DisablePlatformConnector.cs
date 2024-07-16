using System.Reflection;
using HarmonyLib;

namespace Headless.Patches.EngineInitializer;

// Headlessに必要なIPlatformConnectorを実装したものはないので、登録しない
// Steamworks.NETがx64向けにビルドされてて、参照したくない
[HarmonyPatch]
public static class DisablePlatformConnector
{
    public static Type TargetClass()
    {
        return AccessTools.FirstInner(typeof(FrooxEngine.EngineInitializer), t => t.Name.Contains("DisplayClass4_0"));
    }

    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(TargetClass(), "<InitializeFrooxEngine>b__2");
    }

    public static bool Prefix()
    {
        return true;
    }
}
