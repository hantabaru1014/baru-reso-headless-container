using System.Net;
using Elements.Core;
using FrooxEngine;
using Headless.Configuration;
using Headless.GraphQL.Mutations;
using Headless.GraphQL.Queries;
using Headless.GraphQL.Services;
using Headless.GraphQL.Types;
using Headless.GraphQL.Types.Scalars;
using Headless.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Headless;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var appConfig = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        builder.Logging.ClearProviders().AddConsole();

        // Get configs early for Kestrel setup
        var appConfigInstance = appConfig.Get<ApplicationConfig>() ?? new ApplicationConfig();
        var graphqlConfig = appConfig.GetSection("GraphQL").Get<GraphQLConfig>() ?? new GraphQLConfig();

        // Parse gRPC port from RpcHostUrl (default to 5014 if invalid or empty)
        var grpcPort = 5014;
        if (!string.IsNullOrEmpty(appConfigInstance.RpcHostUrl) &&
            Uri.TryCreate(appConfigInstance.RpcHostUrl, UriKind.Absolute, out var grpcUri) &&
            grpcUri.Port > 0)
        {
            grpcPort = grpcUri.Port;
        }

        // Configure Kestrel with separate ports for gRPC (HTTP/2) and GraphQL (HTTP/1.1)
        builder.WebHost.ConfigureKestrel(options =>
        {
            // gRPC endpoint - HTTP/2 only
            options.Listen(IPAddress.IPv6Any, grpcPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });

            // GraphQL endpoint - HTTP/1.1 only (if enabled)
            if (graphqlConfig.Enabled)
            {
                options.Listen(IPAddress.IPv6Any, graphqlConfig.Port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;
                });
            }
        });

        builder.Host.ConfigureServices((hostContext, services) =>
        {
            services.Configure<ApplicationConfig>(appConfig);
            services.Configure<HeadlessStartupConfig>(config =>
            {
                var json = appConfig.GetValue<string>("StartupConfig");
                if (json is not null)
                {
                    config.Parse(json);
                }
            });

            services.AddGrpc();

            services
                .AddSingleton<SystemInfo>()
                .AddSingleton<Engine>()
                .AddSingleton<WorldService>()
                .AddSingleton<FrooxEngineRunnerService>()
                .AddSingleton<IHostedService>(p => p.GetRequiredService<FrooxEngineRunnerService>())
                .AddSingleton<IFrooxEngineRunnerService>(p => p.GetRequiredService<FrooxEngineRunnerService>());

            // GraphQL
            services.Configure<GraphQLConfig>(appConfig.GetSection("GraphQL"));
            services.AddSingleton<FrooxEngineGraphQLService>();
            services
                .AddGraphQLServer()
                .AddQueryType()
                .AddMutationType()
                // Queries
                .AddTypeExtension<WorldQueries>()
                // Mutations
                .AddTypeExtension<SyncMemberMutations>()
                .AddTypeExtension<SlotMutations>()
                .AddTypeExtension<ComponentMutations>()
                // Types
                .AddType<WorldType>()
                .AddType<SlotType>()
                .AddType<ComponentType>()
                .AddType<SyncFieldType>()
                .AddType<SyncRefType>()
                .AddType<GenericSyncMemberType>()
                .AddType<Float3Type>()
                .AddType<FloatQType>()
                .AddType<RefIdType>();
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        // gRPC endpoint (port-specific)
        app.MapGrpcService<GrpcControllerService>().RequireHost($"*:{grpcPort}");
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909")
            .RequireHost($"*:{grpcPort}");

        // GraphQL endpoint (port-specific, conditional)
        if (graphqlConfig.Enabled)
        {
            app.MapGraphQL(graphqlConfig.Path).RequireHost($"*:{graphqlConfig.Port}");
            app.MapGet("/", () => "GraphQL Playground is available at " + graphqlConfig.Path)
                .RequireHost($"*:{graphqlConfig.Port}");
        }

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        UniLog.OnLog += msg => FilterLogMsg(logger, msg);
        UniLog.OnWarning += msg => logger.LogWarning(msg);
        UniLog.OnError += msg => logger.LogError(msg);

        app.Run();
    }

    private static void FilterLogMsg(ILogger logger, string message)
    {
        if (message == "Session updated, forcing status update")
        {
            // logger.LogDebug(message);
            return;
        }
        logger.LogInformation(message);
    }
}
