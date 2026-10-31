namespace CrowdSecManager.Services;

/// <summary>Maps deployment environment variables documented by the container scripts
/// to the ASP.NET configuration keys consumed by the application.</summary>
public static class CrowdSecEnvironmentMapper
{
    private static readonly (string Legacy, string DotNet, string ConfigKey)[] Mappings =
    {
        ("CROWDSEC_CONFIG_DIR", "CrowdSec__ConfigDir", "CrowdSec:ConfigDir"),
        ("CROWDSEC_DATA_DIR", "CrowdSec__DataDir", "CrowdSec:DataDir"),
        ("CROWDSEC_BOUNCER_APIKEY", "CrowdSec__BouncerApiKey", "CrowdSec:BouncerApiKey"),
        ("LOCAL_API_URL", "CrowdSec__LocalApiUrl", "CrowdSec:LocalApiUrl"),
        ("GEOIP_DB_PATH", "GeoIp__DatabasePath", "GeoIp:DatabasePath"),
        ("HOME_LATITUDE", "GeoIp__HomeLatitude", "GeoIp:HomeLatitude"),
        ("HOME_LONGITUDE", "GeoIp__HomeLongitude", "GeoIp:HomeLongitude"),
        ("HOME_LABEL", "GeoIp__HomeLabel", "GeoIp:HomeLabel")
    };

    public static IReadOnlyDictionary<string, string?> ToConfiguration(
        IEnumerable<KeyValuePair<string, string?>> environmentVariables)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in environmentVariables) environment[pair.Key] = pair.Value;
        var mapped = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (legacy, dotNet, configKey) in Mappings)
        {
            if (environment.ContainsKey(dotNet)) continue;
            if (environment.TryGetValue(legacy, out var value) && value is not null)
                mapped[configKey] = value;
        }

        return mapped;
    }
}
