using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EnginePrePatcher.Patches;

/// <summary>
/// In headless mode RenderSystem.HasRenderer is always false, so VideoTexture.Load() never decodes the
/// video and just invokes the onReady callback. The video file downloaded by
/// VideoTextureProvider.LoadFromAsset (AssetManager.GatherAsset with DB_Endpoint.Video) and the stream
/// URL resolved via yt-dlp in LoadFromVideoService are therefore never consumed.
/// Replace the three load paths with bodies that immediately create the VideoTexture and report it as
/// fully loaded (VideoLoaded -> AssetLoadingData.RegisterFullyLoaded), skipping the download and the
/// yt-dlp process entirely while keeping the asset loading tracking consistent.
/// </summary>
public class NoOpVideoDownload : IAssemblyPatch
{
    public string TargetAssemblyPath => "FrooxEngine.dll";

    public IEnumerable<string> RemoveFiles => Array.Empty<string>();

    public bool Patch(AssemblyDefinition assembly)
    {
        var providerType = assembly.MainModule.GetType("FrooxEngine.VideoTextureProvider");
        if (providerType is null)
        {
            Console.WriteLine("FrooxEngine.VideoTextureProvider type not found");
            return false;
        }

        var videoTexField = providerType.Fields.FirstOrDefault(f => f.Name == "_videoTex");
        var createVideoTexture = providerType.Methods.FirstOrDefault(m =>
            m.Name == "CreateVideoTexture" && m.Parameters.Count == 0);
        var videoLoaded = providerType.Methods.FirstOrDefault(m =>
            m.Name == "VideoLoaded" && m.Parameters.Count == 2);
        if (videoTexField is null || createVideoTexture is null || videoLoaded is null)
        {
            Console.WriteLine("VideoTextureProvider members (_videoTex/CreateVideoTexture/VideoLoaded) not found");
            return false;
        }

        string[] targetMethods = ["LoadFromAsset", "LoadFromStreamURL", "LoadFromVideoService"];
        foreach (var name in targetMethods)
        {
            var method = providerType.Methods.FirstOrDefault(m => m.Name == name);
            if (method is null)
            {
                Console.WriteLine($"FrooxEngine.VideoTextureProvider.{name} method not found");
                return false;
            }

            var body = method.Body;
            body.Instructions.Clear();
            body.ExceptionHandlers.Clear();
            body.Variables.Clear();
            body.InitLocals = true;
            var il = body.GetILProcessor();

            // this._videoTex = this.CreateVideoTexture();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, createVideoTexture);
            il.Emit(OpCodes.Stfld, videoTexField);
            // this.VideoLoaded(this._videoTex, assetInstanceChanged: true);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, videoTexField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, videoLoaded);

            if (method.ReturnType.IsValueType)
            {
                // return default(ValueTask);
                var resultVar = new VariableDefinition(method.ReturnType);
                body.Variables.Add(resultVar);
                il.Emit(OpCodes.Ldloca, resultVar);
                il.Emit(OpCodes.Initobj, method.ReturnType);
                il.Emit(OpCodes.Ldloc, resultVar);
            }
            else
            {
                // return Task.CompletedTask;
                var getCompletedTask = method.ReturnType.Resolve().Methods
                    .FirstOrDefault(m => m.Name == "get_CompletedTask");
                if (getCompletedTask is null)
                {
                    Console.WriteLine("Task.get_CompletedTask not found");
                    return false;
                }
                il.Emit(OpCodes.Call, assembly.MainModule.ImportReference(getCompletedTask));
            }
            il.Emit(OpCodes.Ret);

            Console.WriteLine($"Patched FrooxEngine.VideoTextureProvider.{name} to no-op download");
        }
        return true;
    }
}
