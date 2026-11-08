namespace CrowdsecManagerModel;

/// <summary>Geographic point used by the threat-map renderer.</summary>
public sealed class GeoPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Country { get; set; }
    public string? CountryIsoCode { get; set; }
    public string? City { get; set; }
}
