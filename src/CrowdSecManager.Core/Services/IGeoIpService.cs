using CrowdsecManagerModel;

namespace CrowdSecManager.Services;

public interface IGeoIpService
{
    GeoPoint Resolve(string? ip);
    GeoPoint Home { get; }
    bool IsDatabaseAvailable { get; }
}
