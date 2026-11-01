using CrowdSecManager.Models;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class GeoIpServiceTests
{
    [Fact]
    public void FallbackCoordinatesAreStableAcrossServiceInstances()
    {
        using var first = CreateService();
        using var second = CreateService();

        var a = first.Resolve("203.0.113.91");
        var b = second.Resolve("203.0.113.91");

        Assert.Equal(a.Latitude, b.Latitude);
        Assert.Equal(a.Longitude, b.Longitude);
        Assert.InRange(a.Latitude, -90, 90);
        Assert.InRange(a.Longitude, -180, 180);
    }

    [Fact]
    public void Resolve_PreservesUnbracketedIpv6InsteadOfTruncatingAtColon()
    {
        using var service = CreateService();

        var point = service.Resolve("2001:db8::1");

        Assert.Equal("2001:db8::1", point.City);
    }

    [Fact]
    public void Resolve_RecognizesPrivateIpv6AsLocalNetwork()
    {
        using var service = CreateService();

        var point = service.Resolve("fd12:3456::1");

        Assert.Equal("Local network", point.City);
        Assert.Equal("Private", point.Country);
    }

    [Fact]
    public void Resolve_StripsIpv4PortAndCidrBeforeFallback()
    {
        using var service = CreateService();

        var point = service.Resolve("203.0.113.8:443/24");

        Assert.Equal("203.0.113.8", point.City);
    }

    private static GeoIpService CreateService()
        => new(Options.Create(new GeoIpOptions
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "missing-geoip-" + Guid.NewGuid().ToString("N"))
        }), NullLogger<GeoIpService>.Instance);
}
