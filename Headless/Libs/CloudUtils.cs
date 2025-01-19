using SkyFrost.Base;

namespace Headless.Libs;

public static class CloudUtils
{
    private static SkyFrostInterface _cloud = SkyFrostConfig.DEFAULT_PRODUCTION.AssetInterface.Cloud;
    private static AssetInterface _assetInterface = SkyFrostConfig.DEFAULT_PRODUCTION.AssetInterface;

    public static void Setup(AssetInterface assetInterface)
    {
        _assetInterface = assetInterface;
        _cloud = assetInterface.Cloud;
    }

    /// <summary>
    /// resdb:// を直接叩けるURLに変換する。httpを渡したらそのまま返す。
    /// </summary>
    /// <param name="url">変換したいURL</param>
    /// <returns>外でそのまま利用できるURL</returns>
    /// <exception cref="NotSupportedException">対応していないスキーマ</exception>
    public static Uri ResolveURL(Uri url)
    {
        if (url.Scheme == "http" || url.Scheme == "https" || url.Scheme == "ftp")
        {
            return url;
        }
        if (url.Scheme == _assetInterface.DBScheme)
        {
            return _assetInterface.DBToHttp(url, DB_Endpoint.Default);
        }
        throw new NotSupportedException("Unsupported scheme: " + url.Scheme);
    }

    public static string? ResolveURL(string? url)
    {
        return url is not null ? ResolveURL(new Uri(url)).ToString() : null;
    }

    public static string MakeSessionGoURL(string sessionId)
    {
        return _cloud.ApiEndpoint + "/open/session/" + sessionId;
    }
}
