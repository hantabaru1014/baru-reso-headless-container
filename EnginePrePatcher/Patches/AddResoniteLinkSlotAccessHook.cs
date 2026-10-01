using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// ResoniteLink のスロットアクセス判定 (ResoniteLinkTranslator.CanProcessSlot) を Headless 側から
/// 差し替えられるように、ResoniteLinkTranslator に public な CanProcessSlotOverride フィールドを追加し、
/// CanProcessSlot の先頭でフックが設定されていればそちらの結果を返すようにする。
/// 本家の CanProcessSlot は SimpleAvatarProtection を World.LocalUser (= ヘッドレスアカウント) 基準で
/// 判定するため、他ユーザーのアバターが一律で「not accessible」になる。フックで接続ユーザー基準に
/// 置き換えるのが目的。フックが未設定 (null) の場合は元の挙動のまま。
/// CanProcessSlot の形が変わった場合はこのパッチが false を返してビルドが落ちる。
/// </summary>
public class AddResoniteLinkSlotAccessHook : IAssemblyPatch
{
    public const string HookFieldName = "CanProcessSlotOverride";

    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        var translatorType = module.GetType("FrooxEngine.ResoniteLinkTranslator");
        if (translatorType is null)
        {
            Console.WriteLine("FrooxEngine.ResoniteLinkTranslator type not found");
            return false;
        }

        var canProcessSlot = translatorType.Methods.FirstOrDefault(m =>
            m.Name == "CanProcessSlot" && !m.IsStatic && m.HasBody && m.Parameters.Count == 1
            && m.ReturnType.FullName == module.TypeSystem.Boolean.FullName);
        if (canProcessSlot is null)
        {
            Console.WriteLine("ResoniteLinkTranslator.CanProcessSlot(Slot) method not found");
            return false;
        }
        var slotType = canProcessSlot.Parameters[0].ParameterType;
        if (slotType.FullName != "FrooxEngine.Slot")
        {
            Console.WriteLine($"ResoniteLinkTranslator.CanProcessSlot parameter type is {slotType.FullName}, expected FrooxEngine.Slot");
            return false;
        }
        if (translatorType.Fields.Any(f => f.Name == HookFieldName))
        {
            Console.WriteLine($"ResoniteLinkTranslator.{HookFieldName} already exists");
            return false;
        }

        // System.Func<Slot, bool>
        var funcOpenType = new TypeReference("System", "Func`2", module, module.TypeSystem.CoreLibrary);
        funcOpenType.GenericParameters.Add(new GenericParameter("T", funcOpenType));
        funcOpenType.GenericParameters.Add(new GenericParameter("TResult", funcOpenType));
        var funcType = new GenericInstanceType(funcOpenType);
        funcType.GenericArguments.Add(slotType);
        funcType.GenericArguments.Add(module.TypeSystem.Boolean);
        var funcInvoke = new MethodReference("Invoke", funcOpenType.GenericParameters[1], funcType) { HasThis = true };
        funcInvoke.Parameters.Add(new ParameterDefinition(funcOpenType.GenericParameters[0]));

        // public Func<Slot, bool> CanProcessSlotOverride;
        var hookField = new FieldDefinition(HookFieldName, FieldAttributes.Public, funcType);
        translatorType.Fields.Add(hookField);

        // CanProcessSlot の先頭に以下を挿入:
        //   if (CanProcessSlotOverride != null) return CanProcessSlotOverride(slot);
        // 元の先頭命令を分岐先にするので、元の命令列や既存の分岐はそのまま温存される。
        var il = canProcessSlot.Body.GetILProcessor();
        var originalFirst = canProcessSlot.Body.Instructions[0];
        var prologue = new[]
        {
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Ldfld, hookField),
            il.Create(OpCodes.Brfalse, originalFirst),
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Ldfld, hookField),
            il.Create(OpCodes.Ldarg_1),
            il.Create(OpCodes.Callvirt, funcInvoke),
            il.Create(OpCodes.Ret),
        };
        foreach (var instruction in prologue)
        {
            il.InsertBefore(originalFirst, instruction);
        }

        Console.WriteLine($"Added ResoniteLinkTranslator.{HookFieldName} hook to CanProcessSlot");
        return true;
    }
}
