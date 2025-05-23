using Mono.Cecil;

namespace EnginePrePatcher;

public interface IAssemblyPatch
{
    string TargetAssemblyPath { get; }
    IEnumerable<string> RemoveFiles { get; }
    bool Patch(AssemblyDefinition assembly);
}
