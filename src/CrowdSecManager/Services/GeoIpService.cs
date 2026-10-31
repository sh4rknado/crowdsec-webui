using System.Net;
using CrowdSecManager.Models;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

/// <summary>IP -> geo lookup using MaxMind GeoLite2-City.mmdb when present,
/// with a small built-in private-IP / fallback when no database is mounted.</summary>
public interface IGeoIpService
{
    GeoPoint Resolve(string? ip);
    GeoPoint Home { get; }
    bool IsDatabaseAvailable { get; }
}

public sealed class GeoIpService : IGeoIpService, IDisposable
{
    private readonly GeoIpOptions _opt;
    private readonly ILogger<GeoIpService> _log;
    private readonly GeoPoint _home;
    private readonly DatabaseReader? _reader;
    private bool _warnedMissing;

    public GeoIpService(IOptions<GeoIpOptions> opt, ILogger<GeoIpService> log)
    {
        _opt = opt.Value;
        _log = log;
        _home = new GeoPoint
        {
            Latitude = _opt.HomeLatitude,
            Longitude = _opt.HomeLongitude,
            City = _opt.HomeLabel,
            Country = "Home",
            CountryIsoCode = "??"
        };

        try
        {
            if (File.Exists(_opt.DatabasePath))
            {
                _reader = new DatabaseReader(_opt.DatabasePath);
                log.LogInformation("GeoIP database loaded from {Path}", _opt.DatabasePath);
            }
            else
            {
                log.LogWarning("GeoIP database not found at {Path}. IP geolocation will fall back to coarse approximation. " +
                               "Download GeoLite2-City.mmdb from https://www.maxmind.com and mount it at this path.",
                    _opt.DatabasePath);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not open GeoIP database at {Path}", _opt.DatabasePath);
        }
    }

    public bool IsDatabaseAvailable => _reader is not null;

    public GeoPoint Home => _home;

    public GeoPoint Resolve(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return Fallback(ip);

        // Normalize - the API can give us IPs with port or cidr
        ip = ip.Split('/', ':')[0];
        if (!IPAddress.TryParse(ip, out var addr))
            return Fallback(ip);

        if (addr.AddressFamily is System.Net.Sockets.AddressFamily.InterNetworkV6)
            addr = addr.MapToIPv4();

        // RFC1918 / loopback / link-local -> home location
        if (IsPrivate(addr))
            return new GeoPoint
            {
                Latitude = _home.Latitude,
                Longitude = _home.Longitude,
                City = "Local network",
                Country = "Private",
                CountryIsoCode = "PRV"
            };

        if (_reader is null)
            return Fallback(ip);

        try
        {
            CityResponse r = _reader.City(addr);

            return new GeoPoint
            {
                Latitude = r.Location.Latitude ?? _home.Latitude,
                Longitude = r.Location.Longitude ?? _home.Longitude,
                Country = r.Country.Name ?? "Unknown",
                CountryIsoCode = r.Country.IsoCode ?? "??",
                City = r.City.Name
            };
        }
        catch (Exception ex)
        {
            if (!_warnedMissing)
            {
                _log.LogDebug("GeoIP lookup for {Ip} failed: {Msg}", ip, ex.Message);
                _warnedMissing = true;
            }
            return Fallback(ip);
        }
    }

    private static bool IsPrivate(IPAddress a)
    {
        if (IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        if (b.Length != 4) return false;
        // 10.0.0.0/8
        if (b[0] == 10) return true;
        // 172.16/12
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return true;
        // 192.168/16
        if (b[0] == 192 && b[1] == 168) return true;
        // 169.254/16 link-local
        if (b[0] == 169 && b[1] == 254) return true;
        return false;
    }

    /// <summary>Used when no MaxMind DB is mounted. Hash the IP to a deterministic-but-spread
    /// location so the map still looks alive without misleading the operator about precision.</summary>
    private GeoPoint Fallback(string? ip)
    {
        var hash = (ip ?? "0").GetHashCode();
        var lat = (hash & 0xFF) * 0.7 - 90;        // -90..+90
        var lon = ((hash >> 8) & 0x1FF) * 0.7 - 180; // -180..+180
        return new GeoPoint
        {
            Latitude = Math.Round(lat, 4),
            Longitude = Math.Round(lon, 4),
            Country = "Unknown",
            CountryIsoCode = "??",
            City = ip ?? "unknown"
        };
    }

    public void Dispose() => _reader?.Dispose();
}
