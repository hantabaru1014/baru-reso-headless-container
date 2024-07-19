using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

public class FixAssimpLoading : IAssemblyPatch
{
    public string TargetAssemblyPath => "AssimpNet.dll";

    public bool Patch(AssemblyDefinition assembly)
    {
        var libImplTypes = assembly.MainModule.Types.Where(t => t.Name.StartsWith("UnmanagedLinuxLib"));
        foreach (var implType in libImplTypes)
        {
            var getExtMethod = implType.Methods.First(m => m.Name == "get_DllExtension");
            var il = getExtMethod.Resolve().Body.GetILProcessor();
            il.Body.Instructions.Clear();
            il.Emit(OpCodes.Ldstr, ".so.5");
            il.Emit(OpCodes.Ret);
        }
        return true;
    }
}
