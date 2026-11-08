using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CrowdsecManagerModel;
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
