using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Headless.Configuration;
using Headless.Services;

namespace Headless;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var appConfig = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        
        var assemblyResolver = new ResoniteAssemblyResolver([AppDomain.CurrentDomain.BaseDirectory]);
        
        builder.Logging.ClearProviders().AddConsole();
        
        builder.Host.ConfigureServices((hostContext, services) => {
            services.Configure<ApplicationConfig>(appConfig);
            
            services.AddGrpc();

            services
                .AddSingleton(assemblyResolver)
                .AddSingleton<IConfigService, ConfigService>()
                .AddSingleton<SystemInfo>()
                .AddSingleton<Engine>()
                .AddSingleton<WorldService>()
                .AddHostedService<StandaloneFrooxEngineService>();
        });

        var app = builder.Build();
        
        // Configure the HTTP request pipeline.
        app.MapGrpcService<HeadlessControlService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        UniLog.OnLog += msg => logger.LogInformation(msg);
        UniLog.OnWarning += msg => logger.LogWarning(msg);
        UniLog.OnError += msg => logger.LogError(msg);

        ApplyPatches();

        var appConfigInstance = appConfig.Get<ApplicationConfig>() ?? new ApplicationConfig();
        app.Run(appConfigInstance.RpcHostUrl);
    }

    private static void ApplyPatches()
    {
        var harmony = new Harmony("dev.baru.resonite.headless");
        harmony.PatchAllUncategorized();
    }
}
