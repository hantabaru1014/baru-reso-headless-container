using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace EnginePrePatcher.Patches;

public class FixBrotliLoading : IAssemblyPatch
{
    public string TargetAssemblyPath => "Brotli.Core.dll";

    public bool Patch(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        var types = module.Types;
        var nativeLoader = types.First(t => t.Name == "NativeLibraryLoader");
        var nativeLoaderCtor = nativeLoader.GetConstructors().First(m => m.HasParameters);
        var fillDelegate = nativeLoader.Methods.First(m => m.Name == "FillDelegate");

        var brolibTypes = types.Where(t => t.Name == "Brolib64" || t.Name == "Brolib32");
        foreach (var brolibType in brolibTypes)
        {
            var encoderFields = brolibType.Fields.Where(f => f.Name.StartsWith("BrotliEncoder"));
            var decoderFields = brolibType.Fields.Where(f => f.Name.StartsWith("BrotliDecoder"));
            var constructorBody = brolibType.Resolve().GetStaticConstructor().Body;
            var ilProcessor = constructorBody.GetILProcessor();
            ilProcessor.Body.Instructions.Clear();

            var nativeLoaderCtorRef = module.ImportReference(nativeLoaderCtor);
            var fillDelegateRef = module.ImportReference(fillDelegate);

            var encoderLoader = new VariableDefinition(module.ImportReference(nativeLoader));
            constructorBody.Variables.Add(encoderLoader);
            ilProcessor.Emit(OpCodes.Ldstr, "libbrotlienc.so.1");
            ilProcessor.Emit(OpCodes.Newobj, nativeLoaderCtorRef);
            ilProcessor.Emit(OpCodes.Stloc, encoderLoader);
            foreach (var field in encoderFields)
            {
                var fieldRef = module.ImportReference(field);
                ilProcessor.Emit(OpCodes.Ldloc, encoderLoader);
                ilProcessor.Emit(OpCodes.Ldsflda, fieldRef);
                var genericMethod = new GenericInstanceMethod(fillDelegateRef);
                genericMethod.GenericArguments.Add(module.ImportReference(field.FieldType));
                ilProcessor.Emit(OpCodes.Callvirt, genericMethod);
            }

            var decoderLoader = new VariableDefinition(module.ImportReference(nativeLoader));
            constructorBody.Variables.Add(decoderLoader);
            ilProcessor.Emit(OpCodes.Ldstr, "libbrotlidec.so.1");
            ilProcessor.Emit(OpCodes.Newobj, nativeLoaderCtorRef);
            ilProcessor.Emit(OpCodes.Stloc, decoderLoader);
            foreach (var field in decoderFields)
            {
                var fieldRef = module.ImportReference(field);
                ilProcessor.Emit(OpCodes.Ldloc, decoderLoader);
                ilProcessor.Emit(OpCodes.Ldsflda, fieldRef);
                var genericMethod = new GenericInstanceMethod(fillDelegateRef);
                genericMethod.GenericArguments.Add(module.ImportReference(field.FieldType));
                ilProcessor.Emit(OpCodes.Callvirt, genericMethod);
            }

            ilProcessor.Emit(OpCodes.Ret);
        }
        return true;
    }
}
