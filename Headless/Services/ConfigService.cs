using Headless.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Headless.Services;

public interface IConfigService
{
    ResoniteHeadlessConfig Config { get; }
    void SaveConfig(ResoniteHeadlessConfig config);
}

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly ApplicationConfig _appConfig;
    private ResoniteHeadlessConfig? _config;

    public ConfigService(ILogger<ConfigService> logger, IOptions<ApplicationConfig> applicationConfig)
    {
        _logger = logger;
        _appConfig = applicationConfig.Value;
    }

    public ResoniteHeadlessConfig Config
    {
        get {
            if (_config is null)
            {
                var path = Path.Combine(_appConfig.DataDirectoryPath, "Config", "Config.json");
                try
                {
                    _config = JsonConvert.DeserializeObject<ResoniteHeadlessConfig>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex.Message);
                    _logger.LogInformation($"Not found Config.json : {path}");
                }
                if (_config is null)
                {
                    _config = new();
                }
                else
                {
                    _logger.LogInformation($"Loaded Config.json from {path}");
                }
            }
            return _config;
        }
    }

    public void SaveConfig(ResoniteHeadlessConfig config)
    {
        throw new NotImplementedException();
    }
}