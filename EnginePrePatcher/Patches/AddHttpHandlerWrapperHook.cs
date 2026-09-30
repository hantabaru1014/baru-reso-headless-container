using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// SkyFrost の HttpClient (ApiClient.Client / SafeHttpClient) に Headless 側から DelegatingHandler を
/// 差し込めるように、HttpClientUtilities に public static な HandlerWrapper フックを追加し、
/// CreateHttpClient が HttpClient を生成する直前に handler をそのフックへ通す。
/// フックが未設定 (null) の場合は元の挙動のまま。
/// CreateHttpClient の形が変わった場合はこのパッチが false を返してビルドが落ちる。
/// </summary>
public class AddHttpHandlerWrapperHook : IAssemblyPatch
{
    public const string HookFieldName = "HandlerWrapper";

    public string TargetAssemblyPath => "SkyFrost.Base.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        var type = module.GetType("SkyFrost.Base.Utility.HttpClientUtilities");
        if (type is null)
        {
            Console.WriteLine("SkyFrost.Base.Utility.HttpClientUtilities type not found");
            return false;
        }

        var createHttpClient = type.Methods.FirstOrDefault(m => m.Name == "CreateHttpClient" && m.HasBody);
        if (createHttpClient is null)
        {
            Console.WriteLine("HttpClientUtilities.CreateHttpClient method not found");
            return false;
        }

        var newHttpClient = createHttpClient.Body.Instructions.Where(i =>
            i.OpCode == OpCodes.Newobj
            && i.Operand is MethodReference
            {
                DeclaringType.FullName: "System.Net.Http.HttpClient",
                Parameters: [{ ParameterType.FullName: "System.Net.Http.HttpMessageHandler" }]
            }).ToArray();
        if (newHttpClient.Length != 1)
        {
            Console.WriteLine($"Expected exactly one `new HttpClient(HttpMessageHandler)` in CreateHttpClient, found {newHttpClient.Length}");
            return false;
        }
        var httpClientCtor = (MethodReference)newHttpClient[0].Operand;
        var handlerType = httpClientCtor.Parameters[0].ParameterType;

        // System.Func<HttpMessageHandler, HttpMessageHandler>
        var funcOpenType = new TypeReference("System", "Func`2", module, module.TypeSystem.CoreLibrary);
        funcOpenType.GenericParameters.Add(new GenericParameter("T", funcOpenType));
        funcOpenType.GenericParameters.Add(new GenericParameter("TResult", funcOpenType));
        var funcType = new GenericInstanceType(funcOpenType);
        funcType.GenericArguments.Add(handlerType);
        funcType.GenericArguments.Add(handlerType);
        var funcInvoke = new MethodReference("Invoke", funcOpenType.GenericParameters[1], funcType) { HasThis = true };
        funcInvoke.Parameters.Add(new ParameterDefinition(funcOpenType.GenericParameters[0]));

        // public static Func<HttpMessageHandler, HttpMessageHandler> HandlerWrapper;
        var hookField = new FieldDefinition(HookFieldName, FieldAttributes.Public | FieldAttributes.Static, funcType);
        type.Fields.Add(hookField);

        // private static HttpMessageHandler WrapHandler(HttpMessageHandler handler)
        //     => HandlerWrapper is null ? handler : HandlerWrapper(handler);
        var wrapHandler = new MethodDefinition(
            "WrapHandler",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            handlerType);
        wrapHandler.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, handlerType));
        var il = wrapHandler.Body.GetILProcessor();
        var invokeHook = il.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, hookField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue_S, invokeHook);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.Append(invokeHook);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Ret);
        type.Methods.Add(wrapHandler);

        // new HttpClient(handler) -> new HttpClient(WrapHandler(handler))
        // 既存の newobj 命令を call に書き換えてから newobj を後ろへ足すことで、元の命令への分岐があっても必ずフックを通す。
        var callWrap = newHttpClient[0];
        callWrap.OpCode = OpCodes.Call;
        callWrap.Operand = wrapHandler;
        createHttpClient.Body.GetILProcessor().InsertAfter(callWrap, Instruction.Create(OpCodes.Newobj, httpClientCtor));

        Console.WriteLine($"Added HttpClientUtilities.{HookFieldName} hook to CreateHttpClient");
        return true;
    }
}
