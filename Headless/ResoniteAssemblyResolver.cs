using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Mono.Cecil;

namespace Headless;

public class ResoniteAssemblyResolver : DefaultAssemblyResolver
{
    private readonly IReadOnlyDictionary<string, string[]> _knownNativeLibraryMappings = new Dictionary<string, string[]>
    {
        { "assimp", new[] { "libassimp.so.5", "Assimp64.so" } },
        { "freeimage", new[] { "libfreeimage.so.3", "libFreeImage.so" } },
        { "freetype6", new[] { "libfreetype.so.6", "libfreetype6.so" } },
        { "opus", new[] { "libopus.so.0", "libopus.so" } },
        { "dl", new[] { "libdl.so.2" } },
        { "libdl.so", new[] { "libdl.so.2" } },
        { "zlib", new[] { "libz.so.1", "libzlib.so.1", "libzlib.so" } },
    };

    private bool _isDisposed;

    public ResoniteAssemblyResolver()
    {
        foreach (var context in AssemblyLoadContext.All)
        {
            context.ResolvingUnmanagedDll += ResolveNativeAssembly;
        }

        AppDomain.CurrentDomain.AssemblyLoad += AddResolverToAssembly;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveManagedAssembly;

        this.ResolveFailure += ResolveCecilAssembly;
    }

    private AssemblyDefinition? ResolveCecilAssembly(object sender, AssemblyNameReference reference)
    {
        // We only handle non-system assemblies
        return reference.FullName.StartsWith("System")
            ? null
            : SearchDirectory(reference, [AppDomain.CurrentDomain.BaseDirectory], new ReaderParameters());
    }

    private void AddResolverToAssembly(object? sender, AssemblyLoadEventArgs args)
    {
        var context = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly);
        if (context is null || context == AssemblyLoadContext.Default)
        {
            return;
        }

        context.ResolvingUnmanagedDll += ResolveNativeAssembly;
    }

    private IntPtr ResolveNativeAssembly(Assembly sourceAssembly, string assemblyName)
    {
        if (assemblyName is "__Internal")
        {
            return NativeLibrary.GetMainProgramHandle();
        }

        var filename = assemblyName.Contains(".so") || assemblyName.Contains(".dll")
            ? assemblyName
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? $"lib{assemblyName}.so"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? $"{assemblyName}.dll"
                    : assemblyName;

        if (NativeLibrary.TryLoad(assemblyName, out var handle))
        {
            return handle;
        }

        var asLower = assemblyName.ToLowerInvariant();
        if (NativeLibrary.TryLoad(asLower, out handle))
        {
            return handle;
        }

        if (!_knownNativeLibraryMappings.TryGetValue(asLower, out var candidates))
        {
            return IntPtr.Zero;
        }

        return candidates.Any(candidate => NativeLibrary.TryLoad(candidate, out handle))
            ? handle
            : IntPtr.Zero;
    }

    private Assembly? ResolveManagedAssembly(object? sender, ResolveEventArgs args)
    {
        // check if it's already loaded
        var existing = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => a.FullName is not null && a.FullName.Contains(args.Name));

        if (existing is not null)
        {
            return existing;
        }

        var filename = args.Name.Split(',')[0] + ".dll".ToLower();
        var libraryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);

        return Assembly.LoadFrom(libraryPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_isDisposed)
        {
            return;
        }

        foreach (var context in AssemblyLoadContext.All)
        {
            context.ResolvingUnmanagedDll -= ResolveNativeAssembly;
        }

        AppDomain.CurrentDomain.AssemblyLoad -= AddResolverToAssembly;
        AppDomain.CurrentDomain.AssemblyResolve -= ResolveManagedAssembly;

        this.ResolveFailure -= ResolveCecilAssembly;
        _isDisposed = true;
    }
}
