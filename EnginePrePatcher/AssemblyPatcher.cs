using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Pdb;

namespace EnginePrePatcher;

public static class AssemblyPatcher
{
    public static bool Process(string targetDirectoryPath)
    {
        var patches = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => !t.IsInterface && typeof(IAssemblyPatch).IsAssignableFrom(t))
            .ToArray();
        var patchesByAssemblies = new Dictionary<string, List<IAssemblyPatch>>();
        foreach (var patchType in patches)
        {
            var patch = Activator.CreateInstance(patchType) as IAssemblyPatch;
            if (patch is null) continue;

            Console.WriteLine($"Found {patchType.Name}");

            if (patchesByAssemblies.ContainsKey(patch.TargetAssemblyPath))
            {
                patchesByAssemblies[patch.TargetAssemblyPath].Add(patch);
            }
            else
            {
                patchesByAssemblies[patch.TargetAssemblyPath] = new List<IAssemblyPatch>(){ patch };
            }
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(targetDirectoryPath);

        foreach (var kvp in patchesByAssemblies)
        {
            var assemblyPath = Path.Combine(targetDirectoryPath, kvp.Key);
            var symbolsExist = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"));

            using (var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = symbolsExist,
                SymbolReaderProvider = symbolsExist ? new PdbReaderProvider() : null,
                ReadingMode = ReadingMode.Immediate,
                ReadWrite = true,
            }))
            {
                foreach (var patch in kvp.Value)
                {
                    if (!patch.Patch(assembly))
                    {
                        return false;
                    }
                }
                assembly.Write(new WriterParameters
                {
                    // WriteSymbols = symbolsExist,
                    // SymbolWriterProvider = symbolsExist ? new PdbWriterProvider() : null
                });
                Console.WriteLine($"Successfully patched : {assemblyPath}");
            }
        }

        return true;
    }
}
