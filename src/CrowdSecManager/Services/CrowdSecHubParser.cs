using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CrowdSecManager.Core.CommandLine;
using CrowdsecManagerModel;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

/// <summary>Parses the category-grouped object returned by <c>cscli hub list -o json</c>.</summary>
public static class CrowdSecHubParser
{
    private static readonly JsonSerializerOptions HubJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static IReadOnlyList<CsHubItem> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a JSON object grouped by Hub item type.");

        var items = new List<CsHubItem>();
        foreach (var group in document.RootElement.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var element in group.Value.EnumerateArray())
            {
                var item = element.Deserialize<CsHubItem>(HubJsonOptions);
                if (item is null) continue;
                item.Type = group.Name;
                item.Status ??= "";
                item.Installed = !string.IsNullOrWhiteSpace(item.LocalVersion);
                item.Tainted = item.Status.Contains("tainted", StringComparison.OrdinalIgnoreCase);
                item.UpToDate = item.Installed
                    && !item.Status.Contains("update-available", StringComparison.OrdinalIgnoreCase);
                items.Add(item);
            }
        }

        return items;
    }
}
