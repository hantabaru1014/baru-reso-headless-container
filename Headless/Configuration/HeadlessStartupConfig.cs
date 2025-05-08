using Google.Protobuf;
using Headless.Extensions;
using Newtonsoft.Json;

namespace Headless.Configuration;

public class HeadlessStartupConfig
{
    public Rpc.StartupConfig Value { get; set; }

    public HeadlessStartupConfig()
    {
        Value = LoadFromVanillaConfig() ?? new Rpc.StartupConfig();
    }

    private Rpc.StartupConfig? LoadFromVanillaConfig()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.json");
        if (!File.Exists(path))
        {
            return null;
        }
        var config = JsonConvert.DeserializeObject<FrooxEngine.Headless.HeadlessConfig>(File.ReadAllText(path));
        if (config is null)
        {
            return null;
        }
        var converted = new Rpc.StartupConfig();
        if (config.UniverseID is not null)
        {
            converted.UniverseId = config.UniverseID;
        }
        if (config.TickRate > 0)
        {
            converted.TickRate = config.TickRate;
        }
        if (config.MaxConcurrentAssetTransfers > 0)
        {
            converted.MaxConcurrentAssetTransfers = config.MaxConcurrentAssetTransfers;
        }
        if (config.UsernameOverride is not null)
        {
            converted.UsernameOverride = config.UsernameOverride;
        }
        if (config.StartWorlds is not null)
        {
            foreach (var w in config.StartWorlds.Where(w => w.IsEnabled))
            {
                converted.StartWorlds.Add(w.ToProto());
            }
        }
        if (config.AllowedUrlHosts is not null)
        {
            var hosts = config.AllowedUrlHosts.Select(h =>
            {
                string host = h.Trim().ToLower();
                int port = 80;
                if (Uri.TryCreate(host, UriKind.Absolute, out var url) && !string.IsNullOrEmpty(url.Host))
                {
                    host = url.Host;
                    port = url.Port;
                }
                else
                {
                    string[] segments = host.Split(':');
                    switch (segments.Length)
                    {
                        case 1:
                            host = segments[0];
                            break;
                        case 2:
                            host = segments[0];
                            if (segments.Length > 1 && int.TryParse(segments[1], out var p))
                            {
                                port = p;
                            }
                            break;
                    }
                }
                if (string.IsNullOrEmpty(host))
                {
                    return null;
                }
                var types = new List<Rpc.AllowedAccessEntry.Types.AccessType> {
                        Rpc.AllowedAccessEntry.Types.AccessType.Http,
                        Rpc.AllowedAccessEntry.Types.AccessType.Websocket,
                        Rpc.AllowedAccessEntry.Types.AccessType.OscSending
                    };
                if (host == "localhost")
                {
                    types.Add(Rpc.AllowedAccessEntry.Types.AccessType.OscReceiving);
                }
                return new Rpc.AllowedAccessEntry
                {
                    Host = host,
                    Ports = { new int[] { port } },
                    AccessTypes = { types }
                };
            }).Where(h => h != null).Select(h => h!);
            foreach (var h in hosts)
            {
                converted.AllowedUrlHosts.Add(h);
            }
        }
        if (config.AutoSpawnItems is not null)
        {
            foreach (var i in config.AutoSpawnItems)
            {
                converted.AutoSpawnItems.Add(i);
            }
        }

        return converted;
    }

    /// <summary>
    /// json文字列からValueを設定する
    /// </summary>
    /// <param name="json">解析するJSON文字列</param>
    /// <exception cref="InvalidJsonException">JSONの形式が不正な場合</exception>
    /// <exception cref="InvalidProtocolBufferException">変換に失敗した場合(不要なフィールドがある等でも発生)</exception>
    public void Parse(string json)
    {
        var parsed = JsonParser.Default.Parse<Rpc.StartupConfig>(json);
        if (parsed is not null)
        {
            Value = parsed;
        }
    }
}