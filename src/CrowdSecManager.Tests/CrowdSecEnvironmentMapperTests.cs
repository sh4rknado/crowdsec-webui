using CrowdSecManager.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class CrowdSecEnvironmentMapperTests
{
    [Fact]
    public void LegacyContainerVariablesMapToOptionsConsumedByTheApplication()
    {
        var values = new Dictionary<string, string?>
        {
            ["CROWDSEC_CONFIG_DIR"] = "/srv/crowdsec/config",
            ["CROWDSEC_DATA_DIR"] = "/srv/crowdsec/data",
            ["CROWDSEC_BOUNCER_APIKEY"] = "generated-token",
            ["LOCAL_API_URL"] = "http://lapi.internal:8080",
            ["GEOIP_DB_PATH"] = "/srv/geo/city.mmdb",
            ["HOME_LATITUDE"] = "51.5",
            ["HOME_LONGITUDE"] = "-0.12",
            ["HOME_LABEL"] = "London"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CrowdSecEnvironmentMapper.ToConfiguration(values))
            .Build();

        Assert.Equal("/srv/crowdsec/config", configuration["CrowdSec:ConfigDir"]);
        Assert.Equal("/srv/crowdsec/data", configuration["CrowdSec:DataDir"]);
        Assert.Equal("generated-token", configuration["CrowdSec:BouncerApiKey"]);
        Assert.Equal("http://lapi.internal:8080", configuration["CrowdSec:LocalApiUrl"]);
        Assert.Equal("/srv/geo/city.mmdb", configuration["GeoIp:DatabasePath"]);
        Assert.Equal("51.5", configuration["GeoIp:HomeLatitude"]);
        Assert.Equal("-0.12", configuration["GeoIp:HomeLongitude"]);
        Assert.Equal("London", configuration["GeoIp:HomeLabel"]);
    }

    [Fact]
    public void NestedDotNetVariablesTakePrecedenceOverLegacyAliases()
    {
        var values = new Dictionary<string, string?>
        {
            ["CROWDSEC_BOUNCER_APIKEY"] = "legacy-token",
            ["CrowdSec__BouncerApiKey"] = "dotnet-token"
        };

        var mapped = CrowdSecEnvironmentMapper.ToConfiguration(values);

        Assert.DoesNotContain("CrowdSec:BouncerApiKey", mapped.Keys);
    }
}
