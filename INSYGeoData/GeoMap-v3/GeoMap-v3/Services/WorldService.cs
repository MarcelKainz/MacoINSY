using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GeoMap.Services;

/// <summary>Liest wwwroot/data/world.geo.json einmal ein und baut daraus einen SVG-Pfad (Mercator).</summary>
public class WorldService
{
    public string PathData { get; } = "";

    public WorldService(IWebHostEnvironment env)
    {
        var file = env.WebRootFileProvider.GetFileInfo("data/world.geo.json");
        if (!file.Exists) return;

        using var stream = file.CreateReadStream();
        using var doc = JsonDocument.Parse(stream);
        var sb = new StringBuilder();

        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object) continue;
            var type = g.GetProperty("type").GetString();
            var coords = g.GetProperty("coordinates");

            if (type == "Polygon") AddPolygon(sb, coords);
            else if (type == "MultiPolygon")
                foreach (var polygon in coords.EnumerateArray()) AddPolygon(sb, polygon);
        }
        PathData = sb.ToString();
    }

    private static void AddPolygon(StringBuilder sb, JsonElement rings)
    {
        var inv = CultureInfo.InvariantCulture;
        foreach (var ring in rings.EnumerateArray())
        {
            var first = true;
            foreach (var pt in ring.EnumerateArray())
            {
                var lon = pt[0].GetDouble();
                var lat = pt[1].GetDouble();
                sb.Append(first ? 'M' : 'L')
                  .Append(lon.ToString("0.###", inv)).Append(' ')
                  .Append(MapMath.Y(lat).ToString("0.###", inv));
                first = false;
            }
            sb.Append('Z');
        }
    }
}
