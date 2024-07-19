using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Pdb;

namespace EnginePrePatcher;

public static class AssemblyPatcher
{
    public const string PATCHED_LABEL = "EnginePrePatched:";

    public static bool Process(string targetDirectoryPath)
    {
        var thisAssembly = Assembly.GetExecutingAssembly();
        var currentPatcherVersion = thisAssembly.ManifestModule.ModuleVersionId;

        var patches = thisAssembly.GetTypes()
            .Where(t => !t.IsInterface && typeof(IAssemblyPatch).IsAssignableFrom(t))
            .ToArray();
        var patchesByAssemblies = new Dictionary<string, List<IAssemblyPatch>>();
        foreach (var patchType in patches)
        {
            var patch = Activator.CreateInstance(patchType) as IAssemblyPatch;
            if (patch is null) continue;

            Console.WriteLine($"Found Patch: {patchType.Name}");

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

            string? patchedVersion = null;
            using (var assembly = AssemblyDefinition.ReadAssembly(assemblyPath))
            {
                patchedVersion = GetPatcherVersion(assembly);
            }

            if (patchedVersion == currentPatcherVersion.ToString())
            {
                Console.WriteLine($"{assemblyPath} is already patched!");
                continue;
            }
            if (patchedVersion is null)
            {
                File.Copy(assemblyPath, assemblyPath + ".original", true);
            }
            else
            {
                try
                {
                    File.Copy(assemblyPath + ".original", assemblyPath, true);
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"Original dll is not found! : {assemblyPath}");
                    return false;
                }
            }

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

                SetPatcherVersion(assembly, currentPatcherVersion.ToString());
                assembly.Write();
                Console.WriteLine($"Successfully patched : {assemblyPath}");
            }
        }

        return true;
    }

    public static string? GetPatcherVersion(AssemblyDefinition assembly)
    {
        var descAttribute = assembly.CustomAttributes.First(ca => ca.AttributeType.Name == "AssemblyDescriptionAttribute");
        if (descAttribute is not null)
        {
            foreach (var ctorArg in descAttribute.ConstructorArguments)
            {
                string value = (ctorArg.Value as string) ?? "";
                if (value.StartsWith(PATCHED_LABEL))
                {
                    return value.Substring(PATCHED_LABEL.Length);
                }
            }
        }
        return null;
    }

    private static void SetPatcherVersion(AssemblyDefinition assembly, string version)
    {
        var descAttribute = assembly.CustomAttributes.First(ca => ca.AttributeType.Name == "AssemblyDescriptionAttribute");
        var writeValue = PATCHED_LABEL + version;
        if (descAttribute is not null)
        {
            descAttribute.ConstructorArguments.RemoveAt(0);
            descAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.ImportReference(typeof(string)), writeValue));
        }
        else
        {
            var descAttributeConstructor = assembly.MainModule.ImportReference(typeof(AssemblyDescriptionAttribute).GetConstructor([typeof(string)]));
            var newAttribute = new CustomAttribute(descAttributeConstructor);
            newAttribute.ConstructorArguments.Add(new CustomAttributeArgument(assembly.MainModule.ImportReference(typeof(string)), writeValue));
            assembly.MainModule.CustomAttributes.Add(newAttribute);
        }
    }
}
