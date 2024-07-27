using Headless.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Headless.Services;

public interface IConfigService
{
    ResoniteHeadlessConfig Config { get; }
    void SaveConfig();
}

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly ApplicationConfig _appConfig;
    private ResoniteHeadlessConfig? _config;
    private readonly object _syncObj = new object();

    public ConfigService(ILogger<ConfigService> logger, IOptions<ApplicationConfig> applicationConfig)
    {
        _logger = logger;
        _appConfig = applicationConfig.Value;
    }

    public ResoniteHeadlessConfig Config
    {
        get
        {
            if (_config is null)
            {
                var path = Path.Combine(_appConfig.DataDirectoryPath, "Config", "Config.json");
                if (File.Exists(path))
                {
                    try
                    {
                        _config = JsonConvert.DeserializeObject<ResoniteHeadlessConfig>(File.ReadAllText(path));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex.Message);
                        _logger.LogWarning($"Failed load Config.json : {path}");
                    }
                }
                else
                {
                    _logger.LogInformation("Config.json is not exist");
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

    public void SaveConfig()
    {
        if (_config is null) return;

        var path = Path.Combine(_appConfig.DataDirectoryPath, "Config", "Config.json");
        lock (_syncObj)
        {
            var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
            try
            {
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return;
            }
        }
        _logger.LogInformation("Configuration saved!");
    }
}