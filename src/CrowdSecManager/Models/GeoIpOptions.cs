namespace CrowdSecManager.Models;

public sealed class GeoIpOptions
{
    public string DatabasePath { get; set; } = "/var/lib/GeoIP/GeoLite2-City.mmdb";
    public double HomeLatitude { get; set; } = 50.8503;
    public double HomeLongitude { get; set; } = 4.3517;
    public string HomeLabel { get; set; } = "Protected host";
}
