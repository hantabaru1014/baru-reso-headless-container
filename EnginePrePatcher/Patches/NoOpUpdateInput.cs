using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// FrooxEngine.Engine.UpdateInput() throws NullReferenceException in headless mode (no input drivers
/// registered), which leaves Engine.UpdateStep stuck at the InputUpdate stage and prevents the world
/// coroutines from running. Replace its body with a no-op so the update loop advances.
/// </summary>
public class NoOpUpdateInput : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var engineType = assembly.MainModule.GetType("FrooxEngine.Engine");
        if (engineType is null)
        {
            Console.WriteLine("FrooxEngine.Engine type not found");
            return false;
        }

        var updateInput = engineType.Methods.FirstOrDefault(m =>
            m.Name == "UpdateInput" && m.Parameters.Count == 0);
        if (updateInput is null)
        {
            Console.WriteLine("FrooxEngine.Engine.UpdateInput() method not found");
            return false;
        }

        updateInput.Body.Instructions.Clear();
        updateInput.Body.ExceptionHandlers.Clear();
        updateInput.Body.Variables.Clear();
        var il = updateInput.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ret));
        Console.WriteLine("Patched FrooxEngine.Engine.UpdateInput() to no-op");
        return true;
    }
}
