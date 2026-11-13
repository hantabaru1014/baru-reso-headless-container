using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// PhysicsManager.DispatchRefine() runs PhysicsSimulation.Refine (StaticTree.RefitAndRefine) on a
/// WorkProcessor worker thread, and the next frame's PhysicsSimulation.Sync() is supposed to wait for it
/// via OnRefineDone.WaitOne(). However PhysicsSimulation.Initialize creates OnRefineDone as
/// new AutoResetEvent(initialState: true), so the first Sync() after the world is created returns without
/// waiting for the refine. Static collider additions/removals/updates on the main thread then mutate the
/// same static tree and BufferPool concurrently with the refine, which can corrupt the tree (e.g. a cycle
/// in the BVH parent chain that hangs the engine in an infinite loop).
/// Since Sync() always precedes a dispatch, initializing the event as non-signaled makes every Sync()
/// wait for the in-flight refine. This patch rewrites that initialState argument from true to false.
/// </summary>
public class FixPhysicsRefineSync : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var simulationType = assembly.MainModule.GetType("FrooxEngine.PhysicsSimulation");
        if (simulationType is null)
        {
            Console.WriteLine("FrooxEngine.PhysicsSimulation type not found");
            return false;
        }

        var initialize = simulationType.Methods.FirstOrDefault(m => m.Name == "Initialize" && m.HasBody);
        if (initialize is null)
        {
            Console.WriteLine("FrooxEngine.PhysicsSimulation.Initialize method not found");
            return false;
        }

        var ctorCalls = initialize.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Newobj
                && i.Operand is MethodReference ctor
                && ctor.DeclaringType.FullName == "System.Threading.AutoResetEvent"
                && ctor.Parameters.Count == 1
                && ctor.Parameters[0].ParameterType.FullName == "System.Boolean")
            .ToList();
        if (ctorCalls.Count != 1)
        {
            Console.WriteLine($"Expected exactly 1 AutoResetEvent(bool) construction in FrooxEngine.PhysicsSimulation.Initialize, but found {ctorCalls.Count}");
            return false;
        }

        var initialState = ctorCalls[0].Previous;
        if (initialState?.OpCode == OpCodes.Ldc_I4_0)
        {
            // Resonite already creates the event as non-signaled; nothing to fix.
            Console.WriteLine("FrooxEngine.PhysicsSimulation.Initialize already creates OnRefineDone as non-signaled; skipping");
            return true;
        }
        if (initialState is null || initialState.OpCode != OpCodes.Ldc_I4_1)
        {
            // Bail out loudly so we notice when Resonite changes this initialization in an unexpected way.
            Console.WriteLine($"Unexpected AutoResetEvent initialState in FrooxEngine.PhysicsSimulation.Initialize: {initialState?.ToString() ?? "(none)"}");
            return false;
        }

        initialState.OpCode = OpCodes.Ldc_I4_0;

        Console.WriteLine("Patched FrooxEngine.PhysicsSimulation.Initialize to create OnRefineDone as non-signaled");
        return true;
    }
}
