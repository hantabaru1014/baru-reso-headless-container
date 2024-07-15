using System.Text.Json.Serialization;
using SkyFrost.Base;

namespace Headless.Configuration;

public record ResoniteHeadlessConfig
(
    string? Comment = null,
    string? UniverseID = null,
    float TickRate = 60,
    int MaxConcurrentAssetTransfers = 4,
    string? UsernameOverride = null,
    string? LoginCredential = null,
    string? LoginPassword = null,
    IReadOnlyList<WorldStartupParameters>? StartWorlds = null,
    string? DataFolder = null,
    string? CacheFolder = null,
    string? LogsFolder = null,
    IReadOnlyList<string>? AllowedUrlHosts = null,
    IReadOnlyList<Uri>? AutoSpawnItems = null,
    IReadOnlyList<string>? PluginAssemblies = null,
    bool? GeneratePreCache = null,
    int? BackgroundWorkers = null,
    int? PriorityWorkers = null
)
{
    [JsonInclude]
    public static Uri Schema => new
    (
        "https://raw.githubusercontent.com/Yellow-Dog-Man/JSONSchemas/main/schemas/HeadlessConfig.schema.json"
    );

    public ResoniteHeadlessConfig()
        : this(Comment: null)
    {
    }
}