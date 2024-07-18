using Mono.Cecil;

namespace EnginePrePatcher;

public interface IAssemblyPatch
{
    string TargetAssemblyPath { get; }
    bool Patch(AssemblyDefinition assembly);
}
